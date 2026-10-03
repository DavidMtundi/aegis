namespace Aegis.Application.Risk;

using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Risk.Application;
using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Microsoft.Extensions.Logging;

/// <summary>Gathers the cross-module facts the risk model scores (customer profile, alerts, cases, transactions).</summary>
public interface IRiskInputsReader
{
    /// <returns>Null when the customer does not exist in the tenant.</returns>
    Task<RiskInputs?> ReadAsync(
        TenantId tenantId,
        Guid customerId,
        IReadOnlyCollection<int> transactionWindowDays,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}

public sealed record RiskActor(string Id, string? Role, string? CorrelationId)
{
    public static readonly RiskActor System = new("system", null, null);
}

public interface ICustomerRiskService
{
    Task<RiskModel> GetOrCreateActiveModelAsync(TenantId tenantId, RiskActor actor, CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentException">The model is invalid.</exception>
    Task<RiskModel> UpdateModelAsync(
        TenantId tenantId, IReadOnlyList<RiskFactorDefinition> factors, RiskBands bands, RiskActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>Scores the customer with the active model and saves the result.</summary>
    /// <returns>Null when the customer does not exist in the tenant.</returns>
    Task<CustomerRiskScore?> RecalculateAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="RecalculateAsync"/> for side-effect triggers (customer created, alert raised):
    /// a scoring failure is logged and must not fail the change that triggered it.
    /// </summary>
    Task<CustomerRiskScore?> TryRecalculateAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default);
}

public sealed class CustomerRiskService : ICustomerRiskService
{
    private readonly IRiskModelRepository _models;
    private readonly ICustomerRiskScoreRepository _scores;
    private readonly IRiskInputsReader _inputs;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CustomerRiskService> _logger;

    public CustomerRiskService(
        IRiskModelRepository models,
        ICustomerRiskScoreRepository scores,
        IRiskInputsReader inputs,
        IAuditWriter audit,
        IUnitOfWork uow,
        ILogger<CustomerRiskService> logger)
    {
        _logger = logger;
        _models = models;
        _scores = scores;
        _inputs = inputs;
        _audit = audit;
        _uow = uow;
    }

    public async Task<RiskModel> GetOrCreateActiveModelAsync(TenantId tenantId, RiskActor actor, CancellationToken cancellationToken = default)
    {
        var active = await _models.GetActiveAsync(tenantId, cancellationToken);
        if (active is not null) return active;

        var created = RiskModel.CreateDefault(tenantId, actor.Id);
        await _models.AddAsync(created, cancellationToken);
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (UniqueConstraintViolationException)
        {
            // Another request seeded the default model first.
            return await _models.GetActiveAsync(tenantId, cancellationToken)
                   ?? throw new InvalidOperationException("Risk model seeding failed.");
        }
    }

    public async Task<RiskModel> UpdateModelAsync(
        TenantId tenantId, IReadOnlyList<RiskFactorDefinition> factors, RiskBands bands, RiskActor actor,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOrCreateActiveModelAsync(tenantId, actor, cancellationToken);
        var next = RiskModel.CreateNextVersion(current, factors, bands, actor.Id);
        await _models.AddAsync(next, cancellationToken);
        await _audit.AppendAsync(AuditEvent.Create(
            tenantId.Value,
            AuditEventTypes.RISK_MODEL_UPDATED,
            nameof(RiskModel),
            next.Id.ToString(),
            actor.Id,
            actor.Role,
            AuditPayload.Json(new { version = current.Version, factors = current.Factors, bands = current.Bands }),
            AuditPayload.Json(new { version = next.Version, factors = next.Factors, bands = next.Bands }),
            $"Risk model updated to version {next.Version}",
            actor.CorrelationId), cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return next;
    }

    public async Task<CustomerRiskScore?> RecalculateAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
    {
        var model = await GetOrCreateActiveModelAsync(tenantId, actor, cancellationToken);
        var windows = model.Factors
            .Where(f => f.Type == RiskFactorType.TRANSACTION_ACTIVITY && f.WindowDays is > 0)
            .Select(f => f.WindowDays!.Value)
            .ToList();
        var inputs = await _inputs.ReadAsync(tenantId, customerId, windows, DateTimeOffset.UtcNow, cancellationToken);
        if (inputs is null) return null;

        var previous = (await _scores.ListByCustomerAsync(tenantId, customerId, 1, cancellationToken)).FirstOrDefault();
        var score = CustomerRiskScore.Record(tenantId, customerId, model, model.Assess(inputs), trigger, actor.Id);
        await _scores.AddAsync(score, cancellationToken);

        if (previous is null || previous.Band != score.Band)
        {
            await _audit.AppendAsync(AuditEvent.Create(
                tenantId.Value,
                AuditEventTypes.RISK_SCORE_CHANGED,
                "Customer",
                customerId.ToString(),
                actor.Id,
                actor.Role,
                previous is null ? null : AuditPayload.Json(new { score = previous.Score, band = previous.Band.ToString() }),
                AuditPayload.Json(new { score = score.Score, band = score.Band.ToString(), modelVersion = score.ModelVersion }),
                $"Risk band {(previous is null ? "set to" : "changed to")} {score.Band} ({trigger})",
                actor.CorrelationId), cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return score;
    }

    public async Task<CustomerRiskScore?> TryRecalculateAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RecalculateAsync(tenantId, customerId, trigger, actor, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Risk recalculation ({Trigger}) failed for customer {CustomerId}", trigger, customerId);
            return null;
        }
    }
}
