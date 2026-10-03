namespace Aegis.Application.Transactions;

using Aegis.Modules.Alerts.Application;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Customers.Application;
using Aegis.Modules.Features.Application;
using Aegis.Modules.Transactions.Application;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;

public sealed record IngestTransactionPayload(
    string ExternalReference,
    Guid AccountId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Direction,
    string TransactionType,
    string Channel,
    DateTimeOffset Timestamp,
    string? CounterpartyCountry,
    IDictionary<string, string>? Metadata);

public sealed record IngestAndEvaluateRulesCommand(
    TenantId TenantId,
    Guid ActorId,
    string? ActorRole,
    IngestTransactionPayload Payload,
    string CorrelationId);

public sealed record EvaluationSummary(
    Guid RuleId,
    Guid RuleVersionId,
    string RuleCode,
    int RuleVersion,
    bool IsTriggered,
    IReadOnlyDictionary<string, object> Features,
    IReadOnlyList<string> TransactionIds);

public sealed record IngestAndEvaluateRulesResult(
    Guid TransactionId,
    bool WasCreated,
    IReadOnlyList<EvaluationSummary> Evaluations,
    IReadOnlyList<Guid> AlertIds);

public interface IIngestAndEvaluateRules
{
    Task<IngestAndEvaluateRulesResult> ExecuteAsync(
        IngestAndEvaluateRulesCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class IngestAndEvaluateRules : IIngestAndEvaluateRules
{
    private readonly ITransactionRepository _transactions;
    private readonly ICustomerRepository _customers;
    private readonly IAccountRepository _accounts;
    private readonly IFeatureCalculator _features;
    private readonly IAmlRuleVersionRepository _ruleVersions;
    private readonly IRuleEvaluationEngine _engine;
    private readonly IAlertService _alerts;
    private readonly IAlertRepository _alertRepository;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public IngestAndEvaluateRules(
        ITransactionRepository transactions,
        ICustomerRepository customers,
        IAccountRepository accounts,
        IFeatureCalculator features,
        IAmlRuleVersionRepository ruleVersions,
        IRuleEvaluationEngine engine,
        IAlertService alerts,
        IAlertRepository alertRepository,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _transactions = transactions;
        _customers = customers;
        _accounts = accounts;
        _features = features;
        _ruleVersions = ruleVersions;
        _engine = engine;
        _alerts = alerts;
        _alertRepository = alertRepository;
        _audit = audit;
        _uow = uow;
    }

    public async Task<IngestAndEvaluateRulesResult> ExecuteAsync(
        IngestAndEvaluateRulesCommand command,
        CancellationToken cancellationToken = default)
    {
        var payload = command.Payload;
        var existing = await _transactions.GetByTenantAndExternalReferenceAsync(
            command.TenantId, payload.ExternalReference, cancellationToken);
        if (existing is not null)
        {
            return new IngestAndEvaluateRulesResult(
                existing.Id,
                WasCreated: false,
                Evaluations: Array.Empty<EvaluationSummary>(),
                AlertIds: Array.Empty<Guid>());
        }

        var customer = await _customers.GetByTenantAndIdAsync(command.TenantId, payload.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found in tenant.");
        var account = await _accounts.GetByTenantAndIdAsync(command.TenantId, payload.AccountId, cancellationToken)
            ?? throw new InvalidOperationException("Account not found in tenant.");
        if (account.CustomerId.Value != customer.Id)
        {
            throw new InvalidOperationException("Account does not belong to customer.");
        }

        if (!Enum.TryParse<TransactionDirection>(payload.Direction, true, out var direction))
            throw new ArgumentException("Invalid Direction.");
        if (!Enum.TryParse<TransactionType>(payload.TransactionType, true, out var type))
            throw new ArgumentException("Invalid TransactionType.");
        if (!Enum.TryParse<TransactionChannel>(payload.Channel, true, out var channel))
            throw new ArgumentException("Invalid Channel.");

        var money = new Money(payload.Amount, payload.Currency.Trim().ToUpperInvariant());
        if (money.Currency != account.Currency)
            throw new InvalidOperationException(
                $"Transaction currency {money.Currency} does not match account currency {account.Currency}.");
        var tx = CanonicalTransaction.Ingest(
            command.TenantId,
            payload.ExternalReference,
            account.AccountId,
            customer.CustomerId,
            payload.Timestamp,
            money,
            direction,
            type,
            channel,
            payload.CounterpartyCountry,
            payload.Metadata);

        await _transactions.AddAsync(tx, cancellationToken);
        await _audit.AppendAsync(AuditEvent.Create(
            command.TenantId.Value,
            AuditEventTypes.TRANSACTION_INGESTED,
            nameof(CanonicalTransaction),
            tx.Id.ToString(),
            command.ActorId.ToString(),
            command.ActorRole,
            null,
            null,
            "Transaction ingested",
            command.CorrelationId), cancellationToken);

        var activeVersions = await _ruleVersions.GetActiveByTenantAsync(command.TenantId, cancellationToken);

        var evaluations = new List<EvaluationSummary>();
        var alertIds = new List<Guid>();
        var featuresByWindow = new Dictionary<TimeSpan, FeatureCalculationResult>();

        foreach (var version in activeVersions)
        {
            var window = LookbackOf(version.Definition);
            if (!featuresByWindow.TryGetValue(window, out var calculated))
            {
                calculated = await _features.CalculateAsync(
                    command.TenantId,
                    FocusType.CUSTOMER,
                    customer.Id.ToString(),
                    asOfTimestamp: tx.Timestamp,
                    window,
                    cancellationToken);
                featuresByWindow[window] = calculated;
            }

            var featureContext = new DictionaryFeatureContext(calculated.Features.ToDictionary(k => k.Key, v => v.Value));
            var result = await _engine.EvaluateAsync(
                version,
                featureContext,
                focusEntityId: customer.Id.ToString(),
                focusEntityType: FocusType.CUSTOMER.ToString(),
                cancellationToken);

            evaluations.Add(new EvaluationSummary(
                result.RuleId,
                result.RuleVersionId,
                result.RuleCode,
                result.RuleVersion,
                result.IsTriggered,
                calculated.Features,
                calculated.TransactionIds));

            if (!result.IsTriggered)
            {
                continue;
            }

            var evidence = AlertEvidenceMapper.FromEvaluation(
                result,
                calculated.TransactionIds,
                calculated.Features);

            // Bucket by business tx timestamp UTC date (not wall-clock ingest time).
            var upsert = await _alerts.CreateOrGetAsync(
                command.TenantId,
                result,
                evidence,
                version.Definition.Severity,
                version.Definition.RiskScore,
                bucketTimestamp: tx.Timestamp,
                cancellationToken);

            alertIds.Add(upsert.AlertId);

            if (upsert.WasCreated)
            {
                await _audit.AppendAsync(AuditEvent.Create(
                    command.TenantId.Value,
                    AuditEventTypes.ALERT_CREATED,
                    nameof(Alert),
                    upsert.AlertId.ToString(),
                    command.ActorId.ToString(),
                    command.ActorRole,
                    null,
                    AuditPayload.Json(new { ruleCode = result.RuleCode, ruleVersionId = result.RuleVersionId }),
                    "Alert created from rule evaluation",
                    command.CorrelationId), cancellationToken);
            }
        }

        // Single commit: tx + TRANSACTION_INGESTED + alert(s) + ALERT_CREATED (if new).
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.IsTransactionExternalReference)
        {
            // Concurrent duplicate externalReference: DB unique index is the authority.
            var winner = await _transactions.GetByTenantAndExternalReferenceAsync(
                command.TenantId, payload.ExternalReference, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new IngestAndEvaluateRulesResult(
                winner.Id,
                WasCreated: false,
                Evaluations: Array.Empty<EvaluationSummary>(),
                AlertIds: Array.Empty<Guid>());
        }
        catch (UniqueConstraintViolationException ex) when (ex.IsAlertDeduplicationKey)
        {
            // Concurrent alert create for same dedupe key — re-read winners and return.
            var recovered = new List<Guid>();
            foreach (var evaluation in evaluations.Where(e => e.IsTriggered))
            {
                var key = AlertDeduplicationKey.Build(
                    command.TenantId,
                    evaluation.RuleId,
                    evaluation.RuleVersionId,
                    FocusType.CUSTOMER,
                    customer.Id.ToString(),
                    tx.Timestamp);
                var existingAlert = await _alertRepository.GetByTenantAndDeduplicationKeyAsync(
                    command.TenantId, key, cancellationToken);
                if (existingAlert is not null)
                    recovered.Add(existingAlert.Id);
            }

            return new IngestAndEvaluateRulesResult(
                tx.Id,
                WasCreated: true,
                evaluations,
                recovered.Count > 0 ? recovered : alertIds);
        }

        return new IngestAndEvaluateRulesResult(
            tx.Id,
            WasCreated: true,
            evaluations,
            alertIds);
    }

    /// <summary>Rule lookback, capped; falls back to 24h for versions saved before lookback was enforced.</summary>
    private static TimeSpan LookbackOf(RuleDefinition definition)
    {
        if (!RuleDuration.TryParse(definition.Schedule?.Lookback, out var lookback))
            return DefaultLookback;
        return lookback > RuleDuration.MaxLookback ? RuleDuration.MaxLookback : lookback;
    }

    private static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(24);
}