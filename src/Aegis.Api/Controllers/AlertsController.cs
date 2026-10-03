namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Application.Dashboard;
using Aegis.Application.Lookups;
using Aegis.Modules.Alerts.Application;
using Aegis.Modules.Alerts.Domain;
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
[Route("api/v1/alerts")]
public sealed class AlertsController : ControllerBase
{
    private readonly IAlertRepository _alerts;
    private readonly ICaseRepository _cases;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IDisplayNameLookup _names;
    private readonly DashboardOptions _sla;

    public AlertsController(
        IAlertRepository alerts,
        ICaseRepository cases,
        IUserRepository users,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow,
        IDisplayNameLookup names,
        DashboardOptions sla)
    {
        _names = names;
        _sla = sla;
        _alerts = alerts;
        _cases = cases;
        _users = users;
        _tenant = tenant;
        _audit = audit;
        _uow = uow;
    }

    public sealed record AlertResponse(
        Guid Id,
        Guid RuleId,
        Guid RuleVersionId,
        string FocusType,
        string FocusEntityId,
        string Severity,
        int RiskScore,
        string Status,
        string? AssignedTo,
        DateTimeOffset TriggeredAt,
        DateTimeOffset? ResolvedAt,
        string? DismissalReason,
        string DeduplicationKey,
        string RuleName,
        int RuleVersionNumber,
        IReadOnlyDictionary<string, object> EvaluatedValues,
        IReadOnlyList<string> ConditionsSatisfied,
        IReadOnlyList<string> TransactionIds,
        IReadOnlyDictionary<string, object> AdditionalContext,
        string? CustomerName,
        string? CustomerCountry,
        string? AssigneeName);

    public sealed record AlertListResponse(
        IReadOnlyList<AlertResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    public sealed record AssignAlertRequest(string? AssignedTo);
    public sealed record AlertReasonRequest(string? Reason);

    private const int MaxReasonLength = 2000;

    [HttpGet]
    [RequirePermission(Permissions.AlertRead)]
    public async Task<ActionResult<AlertListResponse>> List(
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? view,
        [FromQuery] Guid? customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        AlertStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AlertStatus>(status, true, out var parsedStatus))
                return BadRequest("Invalid status.");
            statusFilter = parsedStatus;
        }

        AlertSeverity? severityFilter = null;
        if (!string.IsNullOrWhiteSpace(severity))
        {
            if (!Enum.TryParse<AlertSeverity>(severity, true, out var parsedSeverity))
                return BadRequest("Invalid severity.");
            severityFilter = parsedSeverity;
        }

        var query = new AlertListQuery(statusFilter, severityFilter, from, to, page, pageSize,
            FocusEntityId: customerId?.ToString());
        switch (view?.Trim().ToLowerInvariant())
        {
            case null or "" or "all":
                break;
            case "open":
                query = query with { OpenOnly = true };
                break;
            case "mine":
                query = query with { OpenOnly = true, AssignedTo = _tenant.UserId.ToString() };
                break;
            case "unassigned":
                query = query with { OpenOnly = true, UnassignedOnly = true };
                break;
            case "pastsla":
                var cutoff = DateTimeOffset.UtcNow.AddDays(-_sla.AlertSlaDays);
                query = query with { OpenOnly = true, To = to is { } t && t < cutoff ? t : cutoff };
                break;
            default:
                return BadRequest("View must be one of: all, open, mine, unassigned, pastSla.");
        }

        var result = await _alerts.ListByTenantAsync(_tenant.TenantId, query, ct);

        return Ok(new AlertListResponse(
            await ToResponsesAsync(result.Items, ct),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.AlertRead)]
    public async Task<ActionResult<AlertResponse>> GetById(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return alert is null ? NotFound() : Ok(await ToResponseAsync(alert, ct));
    }

    [HttpPost("{id:guid}/assign")]
    [RequirePermission(Permissions.AlertAssign)]
    public async Task<ActionResult<AlertResponse>> Assign(Guid id, [FromBody] AssignAlertRequest? request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();
        if (alert.IsClosed) return Conflict($"Alert is {alert.Status} and cannot be changed.");

        var assignee = await _users.FindActiveAssigneeAsync(_tenant.TenantId, request?.AssignedTo, _tenant.UserId, ct);
        if (assignee is null)
            return BadRequest("AssignedTo must be the id of an active user in this tenant.");

        alert.Assign(assignee.Id.ToString());
        await AppendAuditAsync(alert, AuditEventTypes.ALERT_ASSIGNED,
            AuditPayload.Json(new { assignedTo = alert.AssignedTo, assigneeEmail = assignee.Email }), "Alert assigned", ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(alert, ct));
    }

    [HttpPost("{id:guid}/dismiss")]
    [RequirePermission(Permissions.AlertDismiss)]
    public async Task<ActionResult<AlertResponse>> Dismiss(Guid id, [FromBody] AlertReasonRequest? request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request?.Reason))
            return BadRequest("A dismissal reason is required.");
        if (request.Reason.Length > MaxReasonLength)
            return BadRequest($"Reason must be at most {MaxReasonLength} characters.");

        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();
        if (alert.IsClosed) return Conflict($"Alert is {alert.Status} and cannot be changed.");

        alert.Dismiss(_tenant.UserId.ToString(), request.Reason);
        await AppendAuditAsync(alert, AuditEventTypes.ALERT_DISMISSED,
            AuditPayload.Json(new { reason = alert.DismissalReason }), "Alert dismissed", ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(alert, ct));
    }

    [HttpPost("{id:guid}/resolve")]
    [RequirePermission(Permissions.AlertResolve)]
    public async Task<ActionResult<AlertResponse>> Resolve(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();
        if (alert.IsClosed) return Conflict($"Alert is {alert.Status} and cannot be changed.");

        alert.Resolve(_tenant.UserId.ToString());
        await AppendAuditAsync(alert, AuditEventTypes.ALERT_RESOLVED, null, "Alert resolved", ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(alert, ct));
    }

    [HttpPost("{id:guid}/escalate")]
    [RequirePermission(Permissions.AlertEscalate)]
    public async Task<ActionResult<AlertResponse>> Escalate(Guid id, [FromBody] AlertReasonRequest? request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (request?.Reason is { Length: > MaxReasonLength })
            return BadRequest($"Reason must be at most {MaxReasonLength} characters.");

        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();
        if (alert.IsClosed || alert.Status == AlertStatus.ESCALATED)
            return Conflict($"Alert is {alert.Status} and cannot be escalated.");

        alert.Escalate(_tenant.UserId.ToString());
        await AppendAuditAsync(alert, AuditEventTypes.ALERT_ESCALATED,
            AuditPayload.Json(new { reason = request?.Reason?.Trim() }), "Alert escalated", ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(alert, ct));
    }

    private Task AppendAuditAsync(Alert alert, string eventType, string? after, string reason, CancellationToken ct)
        => _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            eventType,
            nameof(Alert),
            alert.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            after,
            reason,
            HttpContext.TraceIdentifier), ct);

    [HttpPost("{id:guid}/create-case")]
    [RequirePermission(Permissions.CaseCreate)]
    public async Task<IActionResult> CreateCase(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();

        Guid? customerId = Guid.TryParse(alert.FocusEntityId, out var parsed) ? parsed : null;
        var priority = alert.Severity switch
        {
            AlertSeverity.CRITICAL => CasePriority.CRITICAL,
            AlertSeverity.HIGH => CasePriority.HIGH,
            AlertSeverity.MEDIUM => CasePriority.MEDIUM,
            _ => CasePriority.LOW
        };
        var title = $"Case for {alert.Evidence.RuleName} ({alert.Id.ToString()[..8]})";
        var complianceCase = ComplianceCase.CreateFromAlert(
            _tenant.TenantId,
            alert.Id,
            title,
            priority,
            customerId);

        await _cases.AddAsync(complianceCase, ct);
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.CASE_CREATED,
            nameof(ComplianceCase),
            complianceCase.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { alertId = alert.Id }),
            "Case created from alert",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);

        return Created($"/api/v1/cases/{complianceCase.Id}", await CasesController.ToResponseAsync(_names, _tenant.TenantId, complianceCase, ct));
    }

    private async Task<AlertResponse> ToResponseAsync(Alert alert, CancellationToken ct)
        => (await ToResponsesAsync(new[] { alert }, ct))[0];

    private async Task<IReadOnlyList<AlertResponse>> ToResponsesAsync(IReadOnlyList<Alert> alerts, CancellationToken ct)
    {
        var customers = await _names.CustomersAsync(_tenant.TenantId,
            DisplayIds.Parse(alerts.Where(a => a.FocusType == FocusType.CUSTOMER).Select(a => a.FocusEntityId)), ct);
        var users = await _names.UserNamesAsync(_tenant.TenantId, DisplayIds.Parse(alerts.Select(a => a.AssignedTo)), ct);
        return alerts.Select(a =>
        {
            var customer = a.FocusType == FocusType.CUSTOMER ? DisplayIds.Find(customers, a.FocusEntityId) : null;
            return ToResponse(a, customer, DisplayIds.Find(users, a.AssignedTo));
        }).ToList();
    }

    private static AlertResponse ToResponse(Alert alert, CustomerLabel? customer, string? assigneeName) => new(
        alert.Id,
        alert.RuleId,
        alert.RuleVersionId,
        alert.FocusType.ToString(),
        alert.FocusEntityId,
        alert.Severity.ToString(),
        alert.RiskScore,
        alert.Status.ToString(),
        alert.AssignedTo,
        alert.TriggeredAt,
        alert.ResolvedAt,
        alert.DismissalReason,
        alert.DeduplicationKey,
        alert.Evidence.RuleName,
        alert.Evidence.RuleVersionNumber,
        alert.Evidence.EvaluatedValues,
        alert.Evidence.ConditionsSatisfied,
        alert.Evidence.TransactionIds,
        alert.Evidence.AdditionalContext,
        customer?.Name,
        customer?.Country,
        assigneeName);
}
