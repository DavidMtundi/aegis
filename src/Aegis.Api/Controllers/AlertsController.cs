namespace Aegis.Api.Controllers;

using Aegis.Modules.Alerts.Application;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Cases.Application;
using Aegis.Modules.Cases.Domain;
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
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public AlertsController(
        IAlertRepository alerts,
        ICaseRepository cases,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _alerts = alerts;
        _cases = cases;
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
        string DeduplicationKey,
        string RuleName,
        int RuleVersionNumber,
        IReadOnlyDictionary<string, object> EvaluatedValues,
        IReadOnlyList<string> ConditionsSatisfied,
        IReadOnlyList<string> TransactionIds,
        IReadOnlyDictionary<string, object> AdditionalContext);

    public sealed record AlertListResponse(
        IReadOnlyList<AlertResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    public sealed record AssignAlertRequest(string? AssignedTo);

    [HttpGet]
    public async Task<ActionResult<AlertListResponse>> List(
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
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

        var result = await _alerts.ListByTenantAsync(
            _tenant.TenantId,
            new AlertListQuery(statusFilter, severityFilter, from, to, page, pageSize),
            ct);

        return Ok(new AlertListResponse(
            result.Items.Select(ToResponse).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlertResponse>> GetById(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return alert is null ? NotFound() : Ok(ToResponse(alert));
    }

    [HttpPost("{id:guid}/assign")]
    public async Task<ActionResult<AlertResponse>> Assign(Guid id, [FromBody] AssignAlertRequest? request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();

        var assignee = string.IsNullOrWhiteSpace(request?.AssignedTo)
            ? _tenant.UserId.ToString()
            : request.AssignedTo.Trim();
        if (assignee.Length > 200)
            return BadRequest("AssignedTo must be at most 200 characters.");

        alert.Assign(assignee);
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.ALERT_ASSIGNED,
            nameof(Alert),
            alert.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            $"{{\"assignedTo\":\"{assignee}\"}}",
            "Alert assigned",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToResponse(alert));
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<ActionResult<AlertResponse>> Dismiss(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();

        alert.Dismiss(_tenant.UserId.ToString());
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.ALERT_DISMISSED,
            nameof(Alert),
            alert.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            null,
            "Alert dismissed",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToResponse(alert));
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<AlertResponse>> Resolve(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (alert is null) return NotFound();

        alert.Resolve(_tenant.UserId.ToString());
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.ALERT_RESOLVED,
            nameof(Alert),
            alert.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            null,
            "Alert resolved",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToResponse(alert));
    }

    [HttpPost("{id:guid}/create-case")]
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
            $"{{\"alertId\":\"{alert.Id}\"}}",
            "Case created from alert",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);

        return Created($"/api/v1/cases/{complianceCase.Id}", CasesController.ToResponse(complianceCase));
    }

    private static AlertResponse ToResponse(Alert alert) => new(
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
        alert.DeduplicationKey,
        alert.Evidence.RuleName,
        alert.Evidence.RuleVersionNumber,
        alert.Evidence.EvaluatedValues,
        alert.Evidence.ConditionsSatisfied,
        alert.Evidence.TransactionIds,
        alert.Evidence.AdditionalContext);
}
