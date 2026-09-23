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
    private readonly IAuditWriter _auditWriter;
    private readonly IAuditEventRepository _auditEvents;
    private readonly ITenantContext _tenantContext;

    public AuditEventsController(
        IAuditWriter auditWriter,
        IAuditEventRepository auditEvents,
        ITenantContext tenantContext)
    {
        _auditWriter = auditWriter;
        _auditEvents = auditEvents;
        _tenantContext = tenantContext;
    }

    public sealed record CreateAuditRequest(string EventType, string EntityType, string EntityId, string? Reason);
    public sealed record AuditEventResponse(
        Guid Id,
        Guid TenantId,
        string EventType,
        string EntityType,
        string EntityId,
        string ActorId,
        DateTimeOffset OccurredAt,
        string? Reason);

    [HttpPost]
    public async Task<ActionResult<AuditEventResponse>> Create([FromBody] CreateAuditRequest request, CancellationToken ct)
    {
        if (!_tenantContext.IsAuthenticated)
        {
            return Unauthorized();
        }

        var audit = AuditEvent.Create(
            _tenantContext.TenantId.Value,
            request.EventType,
            request.EntityType,
            request.EntityId,
            _tenantContext.UserId.ToString(),
            _tenantContext.Roles.FirstOrDefault(),
            null,
            null,
            request.Reason,
            HttpContext.TraceIdentifier);

        await _auditWriter.AppendAsync(audit, ct);
        return CreatedAtAction(nameof(GetById), new { id = audit.Id }, ToResponse(audit));
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
