namespace Aegis.Application.Aml;

using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Modules.Features.Application;
using Aegis.Modules.Transactions.Application;
using Aegis.Shared.Domain;

public sealed record SimulateRuleRequest(int LookbackDays = 30);

public sealed record RuleSimulationResult(
    Guid VersionId,
    Guid RuleId,
    int LookbackDays,
    int CustomersSampled,
    int TransactionsInSample,
    int CandidateTriggers,
    int? ActiveTriggers,
    int? TriggerDelta,
    IReadOnlyList<string> SampleTriggeredCustomerIds);

public interface IRuleSimulator
{
    Task<RuleSimulationResult> SimulateAsync(
        TenantId tenantId,
        Guid versionId,
        int lookbackDays,
        CancellationToken cancellationToken = default);
}

public sealed class RuleSimulator : IRuleSimulator
{
    public const int MinLookbackDays = 1;
    public const int MaxLookbackDays = 90;
    public const int MaxCustomers = 200;
    public const int MaxTransactionsScan = 5000;

    private readonly IAmlRuleVersionRepository _versions;
    private readonly ITransactionRepository _transactions;
    private readonly IFeatureCalculator _features;
    private readonly IRuleEvaluationEngine _engine;

    public RuleSimulator(
        IAmlRuleVersionRepository versions,
        ITransactionRepository transactions,
        IFeatureCalculator features,
        IRuleEvaluationEngine engine)
    {
        _versions = versions;
        _transactions = transactions;
        _features = features;
        _engine = engine;
    }

    public async Task<RuleSimulationResult> SimulateAsync(
        TenantId tenantId,
        Guid versionId,
        int lookbackDays,
        CancellationToken cancellationToken = default)
    {
        if (lookbackDays is < MinLookbackDays or > MaxLookbackDays)
            throw new ArgumentOutOfRangeException(nameof(lookbackDays),
                $"lookbackDays must be between {MinLookbackDays} and {MaxLookbackDays}.");

        var candidate = await _versions.GetByTenantAndIdAsync(tenantId, versionId, cancellationToken)
            ?? throw new KeyNotFoundException("Rule version not found.");

        if (candidate.Status is RuleVersionStatus.RETIRED or RuleVersionStatus.SUPERSEDED)
            throw new InvalidOperationException($"Cannot simulate a rule version in status {candidate.Status}.");

        var active = await _versions.GetActiveByTenantAndRuleIdAsync(tenantId, candidate.RuleId, cancellationToken);
        // Don't compare a version to itself when simulating the live one
        if (active is not null && active.Id == candidate.Id)
            active = null;

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-lookbackDays);
        var latestByCustomer = new Dictionary<Guid, DateTimeOffset>();
        var scanned = 0;
        // List API clamps page size to 100 — page until we hit the scan cap.
        for (var pageNum = 1; scanned < MaxTransactionsScan; pageNum++)
        {
            var page = await _transactions.ListByTenantAsync(
                tenantId,
                new TransactionListQuery(From: from, To: to, Page: pageNum, PageSize: 100),
                cancellationToken);
            if (page.Items.Count == 0) break;

            foreach (var tx in page.Items)
            {
                scanned++;
                if (!latestByCustomer.TryGetValue(tx.CustomerId.Value, out var prev) || tx.Timestamp > prev)
                    latestByCustomer[tx.CustomerId.Value] = tx.Timestamp;
                if (scanned >= MaxTransactionsScan) break;
            }

            if (page.Items.Count < page.PageSize || scanned >= page.TotalCount) break;
        }

        var sample = latestByCustomer
            .OrderByDescending(kv => kv.Value)
            .Take(MaxCustomers)
            .ToList();

        var candidateWindow = LookbackOf(candidate.Definition);
        var activeWindow = active is null ? (TimeSpan?)null : LookbackOf(active.Definition);

        var candidateHits = 0;
        var activeHits = 0;
        var sampleIds = new List<string>();

        foreach (var (customerId, asOf) in sample)
        {
            var candFeatures = await _features.CalculateAsync(
                tenantId, FocusType.CUSTOMER, customerId.ToString(), asOf, candidateWindow, cancellationToken);
            var candResult = await _engine.EvaluateAsync(
                candidate,
                new DictionaryFeatureContext(candFeatures.Features.ToDictionary(k => k.Key, v => v.Value)),
                customerId.ToString(),
                FocusType.CUSTOMER.ToString(),
                cancellationToken);

            if (candResult.IsTriggered)
            {
                candidateHits++;
                if (sampleIds.Count < 20)
                    sampleIds.Add(customerId.ToString());
            }

            if (active is not null && activeWindow is not null)
            {
                var actFeatures = await _features.CalculateAsync(
                    tenantId, FocusType.CUSTOMER, customerId.ToString(), asOf, activeWindow.Value, cancellationToken);
                var actResult = await _engine.EvaluateAsync(
                    active,
                    new DictionaryFeatureContext(actFeatures.Features.ToDictionary(k => k.Key, v => v.Value)),
                    customerId.ToString(),
                    FocusType.CUSTOMER.ToString(),
                    cancellationToken);
                if (actResult.IsTriggered)
                    activeHits++;
            }
        }

        int? activeTriggers = active is null ? null : activeHits;
        int? delta = activeTriggers is null ? null : candidateHits - activeTriggers;

        return new RuleSimulationResult(
            candidate.Id,
            candidate.RuleId,
            lookbackDays,
            sample.Count,
            scanned,
            candidateHits,
            activeTriggers,
            delta,
            sampleIds);
    }

    private static TimeSpan LookbackOf(RuleDefinition definition)
    {
        if (!RuleDuration.TryParse(definition.Schedule?.Lookback, out var lookback))
            return TimeSpan.FromHours(24);
        return lookback > RuleDuration.MaxLookback ? RuleDuration.MaxLookback : lookback;
    }
}
