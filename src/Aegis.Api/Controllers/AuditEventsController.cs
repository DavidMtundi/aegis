namespace Aegis.Api.Controllers;

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
        string? Reason);

    /// <summary>
    /// Audit events are append-only via trusted application paths (ingest, bootstrap, login, etc.).
    /// Clients may read tenant-scoped events; they must not create arbitrary audit rows.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditEventResponse>>> List(CancellationToken ct)
    {
        if (!_tenantContext.IsAuthenticated)
        {
            return Unauthorized();
        }

        var events = await _auditEvents.ListByTenantAsync(_tenantContext.TenantId.Value, take: 100, ct);
        return Ok(events.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
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
        audit.Reason);
}
