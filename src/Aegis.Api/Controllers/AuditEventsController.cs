namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/audit-events")]
public sealed class AuditEventsController : ControllerBase
{
    private readonly IAuditEventRepository _auditEvents;
    private readonly ITenantContext _tenantContext;

    public AuditEventsController(
        IAuditEventRepository auditEvents,
        ITenantContext tenantContext)
    {
        _auditEvents = auditEvents;
        _tenantContext = tenantContext;
    }

    public sealed record AuditEventResponse(
        Guid Id,
        Guid TenantId,
        string EventType,
        string EntityType,
        string EntityId,
        string ActorId,
        DateTimeOffset OccurredAt,
        string? Reason,
        string? ActorRole,
        string? BeforeState,
        string? AfterState,
        string? CorrelationId);

    public sealed record AuditEventListResponse(
        IReadOnlyList<AuditEventResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    /// <summary>
    /// Audit events are append-only via trusted application paths (ingest, bootstrap, login, etc.).
    /// Clients may read tenant-scoped events; they must not create arbitrary audit rows.
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.AuditRead)]
    public async Task<ActionResult<AuditEventListResponse>> List(
        [FromQuery] string? eventType,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? actorId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_tenantContext.IsAuthenticated)
        {
            return Unauthorized();
        }

        if (from is not null && to is not null && from > to)
        {
            return BadRequest("'from' must be before 'to'.");
        }

        var result = await _auditEvents.QueryAsync(
            _tenantContext.TenantId.Value,
            new AuditEventQuery(eventType, entityType, entityId, actorId, from, to, page, pageSize),
            ct);
        return Ok(new AuditEventListResponse(
            result.Items.Select(ToResponse).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.AuditRead)]
    public async Task<ActionResult<AuditEventResponse>> GetById(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsAuthenticated)
        {
            return Unauthorized();
        }

        var audit = await _auditEvents.GetByTenantAndIdAsync(_tenantContext.TenantId.Value, id, ct);
        if (audit is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(audit));
    }

    private static AuditEventResponse ToResponse(AuditEvent audit) => new(
        audit.Id,
        audit.TenantId,
        audit.EventType,
        audit.EntityType,
        audit.EntityId,
        audit.ActorId,
        audit.OccurredAt,
        audit.Reason,
        audit.ActorRole,
        audit.BeforeState,
        audit.AfterState,
        audit.CorrelationId);
}
