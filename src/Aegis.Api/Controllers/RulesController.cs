namespace Aegis.Api.Controllers;

using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/rules")]
public sealed class RulesController : ControllerBase
{
    private readonly IAmlRuleVersionRepository _ruleVersions;
    private readonly ITenantContext _tenant;

    public RulesController(IAmlRuleVersionRepository ruleVersions, ITenantContext tenant)
    {
        _ruleVersions = ruleVersions;
        _tenant = tenant;
    }

    public sealed record RuleSummaryResponse(
        Guid RuleId,
        Guid RuleVersionId,
        string Code,
        string Name,
        int VersionNumber,
        string Status,
        string Severity,
        int RiskScore,
        DateTimeOffset? EffectiveFrom);

    public sealed record RuleDetailResponse(
        Guid RuleId,
        Guid RuleVersionId,
        string Code,
        string Name,
        int VersionNumber,
        string Status,
        string Severity,
        int RiskScore,
        DateTimeOffset? EffectiveFrom,
        RuleDefinition Definition);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RuleSummaryResponse>>> List(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var versions = await _ruleVersions.GetActiveByTenantAsync(_tenant.TenantId, ct);
        return Ok(versions
            .OrderBy(v => v.RuleCode)
            .Select(ToSummary)
            .ToList());
    }

    [HttpGet("{ruleId:guid}")]
    public async Task<ActionResult<RuleDetailResponse>> GetByRuleId(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetActiveByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        return version is null ? NotFound() : Ok(ToDetail(version));
    }

    private static RuleSummaryResponse ToSummary(AmlRuleVersion v) => new(
        v.RuleId,
        v.Id,
        v.RuleCode,
        v.RuleName,
        v.VersionNumber,
        v.Status.ToString(),
        v.Definition.Severity.ToString(),
        v.Definition.RiskScore,
        v.EffectiveFrom);

    private static RuleDetailResponse ToDetail(AmlRuleVersion v) => new(
        v.RuleId,
        v.Id,
        v.RuleCode,
        v.RuleName,
        v.VersionNumber,
        v.Status.ToString(),
        v.Definition.Severity.ToString(),
        v.Definition.RiskScore,
        v.EffectiveFrom,
        v.Definition);
}
