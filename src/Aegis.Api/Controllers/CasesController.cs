namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Application.Cases;
using Aegis.Application.Lookups;
using Aegis.Modules.Alerts.Application;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Cases.Application;
using Aegis.Modules.Cases.Domain;
using Aegis.Modules.Identity.Application;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/cases")]
public sealed class CasesController : ControllerBase
{
    private const int MaxReasonLength = 2000;

    private readonly ICaseRepository _cases;
    private readonly IAlertRepository _alerts;
    private readonly IUserRepository _users;
    private readonly IAuditEventRepository _auditEvents;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IDisplayNameLookup _names;

    public CasesController(
        ICaseRepository cases,
        IAlertRepository alerts,
        IUserRepository users,
        IAuditEventRepository auditEvents,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow,
        IDisplayNameLookup names)
    {
        _names = names;
        _cases = cases;
        _alerts = alerts;
        _users = users;
        _auditEvents = auditEvents;
        _tenant = tenant;
        _audit = audit;
        _uow = uow;
    }

    public sealed record CaseNoteResponse(Guid Id, string Text, string AuthorId, DateTimeOffset CreatedAt);

    public sealed record CaseResponse(
        Guid Id,
        Guid? CustomerId,
        string Title,
        string Status,
        string Priority,
        string? AssignedTo,
        DateTimeOffset OpenedAt,
        DateTimeOffset? ClosedAt,
        string? Disposition,
        string? Conclusion,
        IReadOnlyList<Guid> LinkedAlertIds,
        IReadOnlyList<CaseNoteResponse> Notes,
        string? CustomerName,
        string? AssigneeName,
        IReadOnlyList<AlertLabel> LinkedAlerts);

    public sealed record CaseListResponse(
        IReadOnlyList<CaseResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    public sealed record AssignCaseRequest(string? AssignedTo);
    public sealed record AddNoteRequest(string Text);
    public sealed record CloseCaseRequest(string Disposition, string Conclusion);
    public sealed record LinkAlertRequest(Guid AlertId);
    public sealed record EscalateCaseRequest(string? Reason);

    [HttpGet]
    [RequirePermission(Permissions.CaseRead)]
    public async Task<ActionResult<CaseListResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var result = await _cases.ListByTenantAsync(_tenant.TenantId, new CaseListQuery(page, pageSize), ct);
        return Ok(new CaseListResponse(
            await ToResponsesAsync(_names, _tenant.TenantId, result.Items, ct),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.CaseRead)]
    public async Task<ActionResult<CaseResponse>> Get(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return c is null ? NotFound() : Ok(await ToResponseAsync(_names, _tenant.TenantId, c, ct));
    }

    [HttpPost("{id:guid}/assign")]
    [RequirePermission(Permissions.CaseUpdate)]
    public async Task<ActionResult<CaseResponse>> Assign(Guid id, [FromBody] AssignCaseRequest? request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();
        if (c.Status == CaseStatus.CLOSED) return Conflict("Cannot assign a closed case.");

        var assignee = await _users.FindActiveAssigneeAsync(_tenant.TenantId, request?.AssignedTo, _tenant.UserId, ct);
        if (assignee is null)
            return BadRequest("AssignedTo must be the id of an active user in this tenant.");

        c.Assign(assignee.Id.ToString());
        await AppendAuditAsync(c, AuditEventTypes.CASE_ASSIGNED,
            AuditPayload.Json(new { assignedTo = c.AssignedTo, assigneeEmail = assignee.Email }), "Case assigned", ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(_names, _tenant.TenantId, c, ct));
    }

    [HttpPost("{id:guid}/alerts")]
    [RequirePermission(Permissions.CaseUpdate)]
    public async Task<ActionResult<CaseResponse>> LinkAlert(Guid id, [FromBody] LinkAlertRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();
        if (c.Status == CaseStatus.CLOSED) return Conflict("Cannot link alerts to a closed case.");

        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, request.AlertId, ct);
        if (alert is null) return NotFound("Alert not found.");

        if (c.LinkAlert(alert.Id))
        {
            await AppendAuditAsync(c, AuditEventTypes.CASE_ALERT_LINKED,
                AuditPayload.Json(new { alertId = alert.Id }), "Alert linked to case", ct);
            await _uow.SaveChangesAsync(ct);
        }
        return Ok(await ToResponseAsync(_names, _tenant.TenantId, c, ct));
    }

    [HttpPost("{id:guid}/escalate")]
    [RequirePermission(Permissions.CaseUpdate)]
    public async Task<ActionResult<CaseResponse>> Escalate(Guid id, [FromBody] EscalateCaseRequest? request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (request?.Reason is { Length: > MaxReasonLength })
            return BadRequest($"Reason must be at most {MaxReasonLength} characters.");

        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();
        if (c.Status is CaseStatus.CLOSED or CaseStatus.ESCALATED)
            return Conflict($"Case is {c.Status} and cannot be escalated.");

        c.Escalate();
        await AppendAuditAsync(c, AuditEventTypes.CASE_ESCALATED,
            AuditPayload.Json(new { reason = request?.Reason?.Trim() }), "Case escalated", ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(_names, _tenant.TenantId, c, ct));
    }

    [HttpGet("{id:guid}/timeline")]
    [RequirePermission(Permissions.CaseRead)]
    public async Task<ActionResult<IReadOnlyList<CaseTimelineEntry>>> Timeline(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();

        var events = await _auditEvents.ListByEntityIdsAsync(_tenant.TenantId.Value, CaseTimeline.EntityIds(c), ct);
        return Ok(CaseTimeline.Build(c, events));
    }

    private Task AppendAuditAsync(ComplianceCase c, string eventType, string? after, string reason, CancellationToken ct)
        => _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            eventType,
            nameof(ComplianceCase),
            c.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            after,
            reason,
            HttpContext.TraceIdentifier), ct);

    [HttpPost("{id:guid}/notes")]
    [RequirePermission(Permissions.CaseUpdate)]
    public async Task<ActionResult<CaseResponse>> AddNote(Guid id, [FromBody] AddNoteRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Text is required.");

        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();

        if (c.Status == CaseStatus.CLOSED) return Conflict("Cannot add notes to a closed case.");

        c.AddNote(request.Text, _tenant.UserId.ToString());
        await AppendAuditAsync(c, AuditEventTypes.CASE_NOTE_ADDED,
            AuditPayload.Json(new { noteId = c.Notes[^1].Id }), "Case note added", ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(_names, _tenant.TenantId, c, ct));
    }

    [HttpPost("{id:guid}/close")]
    [RequirePermission(Permissions.CaseClose)]
    public async Task<ActionResult<CaseResponse>> Close(Guid id, [FromBody] CloseCaseRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Conclusion))
            return BadRequest("Conclusion is required.");
        if (!Enum.TryParse<CaseDisposition>(request.Disposition, true, out var disposition))
            return BadRequest("Invalid disposition.");

        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();
        if (c.Status == CaseStatus.CLOSED) return Conflict("Case is already closed.");

        c.Close(disposition, request.Conclusion);

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.CASE_CLOSED,
            nameof(ComplianceCase),
            c.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { disposition = disposition.ToString() }),
            request.Conclusion.Trim(),
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(_names, _tenant.TenantId, c, ct));
    }

    internal static async Task<CaseResponse> ToResponseAsync(
        IDisplayNameLookup names, TenantId tenantId, ComplianceCase c, CancellationToken ct)
        => (await ToResponsesAsync(names, tenantId, new[] { c }, ct))[0];

    internal static async Task<IReadOnlyList<CaseResponse>> ToResponsesAsync(
        IDisplayNameLookup names, TenantId tenantId, IReadOnlyList<ComplianceCase> cases, CancellationToken ct)
    {
        var customers = await names.CustomersAsync(tenantId, cases.Where(c => c.CustomerId.HasValue).Select(c => c.CustomerId!.Value), ct);
        var users = await names.UserNamesAsync(tenantId, DisplayIds.Parse(cases.Select(c => c.AssignedTo)), ct);
        var alerts = await names.AlertsAsync(tenantId, cases.SelectMany(c => c.LinkedAlertIds), ct);
        return cases.Select(c => ToResponse(
            c,
            c.CustomerId is Guid customerId && customers.TryGetValue(customerId, out var customer) ? customer.Name : null,
            DisplayIds.Find(users, c.AssignedTo),
            c.LinkedAlertIds.Where(alerts.ContainsKey).Select(id => alerts[id]).ToList())).ToList();
    }

    private static CaseResponse ToResponse(
        ComplianceCase c, string? customerName, string? assigneeName, IReadOnlyList<AlertLabel> linkedAlerts) => new(
        c.Id,
        c.CustomerId,
        c.Title,
        c.Status.ToString(),
        c.Priority.ToString(),
        c.AssignedTo,
        c.OpenedAt,
        c.ClosedAt,
        c.Disposition?.ToString(),
        c.Conclusion,
        c.LinkedAlertIds.ToList(),
        c.Notes.Select(n => new CaseNoteResponse(n.Id, n.Text, n.AuthorId, n.CreatedAt)).ToList(),
        customerName,
        assigneeName,
        linkedAlerts);
}
