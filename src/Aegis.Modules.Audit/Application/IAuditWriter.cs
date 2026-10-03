namespace Aegis.Modules.Audit.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Audit.Domain;

public interface IAuditWriter
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed record AuditEventQuery(
    string? EventType = null,
    string? EntityType = null,
    string? EntityId = null,
    string? ActorId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditEventListResult(IReadOnlyList<AuditEvent> Items, int TotalCount, int Page, int PageSize);

public interface IAuditEventRepository
{
    Task<AuditEvent?> GetByTenantAndIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEvent>> ListByTenantAsync(Guid tenantId, int take = 100, CancellationToken cancellationToken = default);
    /// <summary>Newest first. Page size is clamped to 1–200.</summary>
    Task<AuditEventListResult> QueryAsync(Guid tenantId, AuditEventQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEvent>> ListByEntityIdsAsync(Guid tenantId, IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default);
}
