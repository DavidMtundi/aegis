namespace Aegis.Api.Controllers;

using Aegis.Modules.Alerts.Application;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/alerts")]
public sealed class AlertsController : ControllerBase
{
    private readonly IAlertRepository _alerts;
    private readonly ITenantContext _tenant;

    public AlertsController(IAlertRepository alerts, ITenantContext tenant)
    {
        _alerts = alerts;
        _tenant = tenant;
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
        DateTimeOffset TriggeredAt,
        string DeduplicationKey,
        string RuleName,
        int RuleVersionNumber,
        IReadOnlyDictionary<string, object> EvaluatedValues,
        IReadOnlyList<string> ConditionsSatisfied,
        IReadOnlyList<string> TransactionIds,
        IReadOnlyDictionary<string, object> AdditionalContext);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AlertResponse>>> List(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alerts = await _alerts.ListByTenantAsync(_tenant.TenantId, take: 100, ct);
        return Ok(alerts.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlertResponse>> GetById(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var alert = await _alerts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return alert is null ? NotFound() : Ok(ToResponse(alert));
    }

    private static AlertResponse ToResponse(Modules.Alerts.Domain.Alert alert) => new(
        alert.Id,
        alert.RuleId,
        alert.RuleVersionId,
        alert.FocusType.ToString(),
        alert.FocusEntityId,
        alert.Severity.ToString(),
        alert.RiskScore,
        alert.Status.ToString(),
        alert.TriggeredAt,
        alert.DeduplicationKey,
        alert.Evidence.RuleName,
        alert.Evidence.RuleVersionNumber,
        alert.Evidence.EvaluatedValues,
        alert.Evidence.ConditionsSatisfied,
        alert.Evidence.TransactionIds,
        alert.Evidence.AdditionalContext);
}
