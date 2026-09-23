namespace Aegis.Modules.Alerts.Application;

using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Shared.Domain;

public sealed record AlertUpsertResult(Guid AlertId, bool WasCreated);

public sealed record AlertListQuery(
    AlertStatus? Status = null,
    AlertSeverity? Severity = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AlertListResult(IReadOnlyList<Alert> Items, int TotalCount, int Page, int PageSize);

public interface IAlertRepository
{
    Task<Alert?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<Alert?> GetByTenantAndDeduplicationKeyAsync(TenantId tenantId, string deduplicationKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Alert>> ListByTenantAsync(TenantId tenantId, int take = 100, CancellationToken cancellationToken = default);
    Task<AlertListResult> ListByTenantAsync(TenantId tenantId, AlertListQuery query, CancellationToken cancellationToken = default);
    Task AddAsync(Alert alert, CancellationToken cancellationToken = default);
}

public interface IAlertService
{
    /// <summary>
    /// AML may return TRIGGERED many times; this decides create vs return existing.
    /// Dedupe bucket uses the UTC calendar date of <paramref name="bucketTimestamp"/>
    /// (prefer the triggering transaction's business Timestamp so ingest delay does not shift the bucket).
    /// Does not call SaveChanges — caller owns the unit of work.
    /// </summary>
    Task<AlertUpsertResult> CreateOrGetAsync(
        TenantId tenantId,
        RuleEvaluationResult triggered,
        AlertEvidence evidence,
        AlertSeverity severity,
        int riskScore,
        DateTimeOffset bucketTimestamp,
        CancellationToken cancellationToken = default);
}

public static class AlertDeduplicationKey
{
    public static string Build(
        TenantId tenantId,
        Guid ruleId,
        Guid ruleVersionId,
        FocusType focusType,
        string focusEntityId,
        DateTimeOffset bucketTimestamp)
    {
        var day = bucketTimestamp.UtcDateTime.ToString("yyyy-MM-dd");
        return $"{tenantId.Value:D}:{ruleId:D}:{ruleVersionId:D}:{focusType}:{focusEntityId}:{day}";
    }
}
