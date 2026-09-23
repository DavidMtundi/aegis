namespace Aegis.Api.Controllers;

using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/rules")]
public sealed class RulesController : ControllerBase
{
    private readonly IAmlRuleRepository _rules;
    private readonly IAmlRuleVersionRepository _ruleVersions;
    private readonly RuleDefinitionValidator _validator;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public RulesController(
        IAmlRuleRepository rules,
        IAmlRuleVersionRepository ruleVersions,
        RuleDefinitionValidator validator,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _rules = rules;
        _ruleVersions = ruleVersions;
        _validator = validator;
        _tenant = tenant;
        _audit = audit;
        _uow = uow;
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

    public sealed record CreateDraftRequest(RuleDefinition Definition);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RuleSummaryResponse>>> List(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var versions = await _ruleVersions.GetActiveByTenantAsync(_tenant.TenantId, ct);
        return Ok(versions.OrderBy(v => v.RuleCode).Select(ToSummary).ToList());
    }

    [HttpGet("{ruleId:guid}")]
    public async Task<ActionResult<RuleDetailResponse>> GetByRuleId(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetActiveByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        return version is null ? NotFound() : Ok(ToDetail(version));
    }

    [HttpGet("{ruleId:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<RuleSummaryResponse>>> ListVersions(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var versions = await _ruleVersions.ListByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        return Ok(versions.Select(ToSummary).ToList());
    }

    [HttpPost("{ruleId:guid}/versions")]
    public async Task<ActionResult<RuleDetailResponse>> CreateDraft(Guid ruleId, [FromBody] CreateDraftRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (!TenantAuthorization.CanManageRules(_tenant)) return Forbid();
        if (request.Definition is null) return BadRequest("Definition is required.");

        var validation = _validator.Validate(request.Definition);
        if (!validation.IsValid)
            return BadRequest(string.Join("; ", validation.Errors));

        var active = await _ruleVersions.GetActiveByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        if (active is null) return NotFound();

        var next = await _ruleVersions.GetMaxVersionNumberAsync(_tenant.TenantId, ruleId, ct) + 1;
        var draft = AmlRuleVersion.Create(
            ruleId,
            _tenant.TenantId,
            next,
            request.Definition,
            _tenant.UserId.ToString());

        await _ruleVersions.AddAsync(draft, ct);
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_CREATED,
            nameof(AmlRuleVersion),
            draft.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            $"{{\"ruleId\":\"{ruleId}\",\"version\":{next}}}",
            "Draft rule version created",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Created($"/api/v1/rules/{ruleId}/versions", ToDetail(draft));
    }

    [HttpPost("versions/{versionId:guid}/activate")]
    public async Task<ActionResult<RuleDetailResponse>> Activate(Guid versionId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (!TenantAuthorization.CanManageRules(_tenant)) return Forbid();

        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        if (version is null) return NotFound();
        if (version.Status == RuleVersionStatus.ACTIVE)
            return Ok(ToDetail(version));

        var currentActive = await _ruleVersions.GetActiveByTenantAndRuleIdAsync(_tenant.TenantId, version.RuleId, ct);
        // GetActive uses AsNoTracking — need tracked entity to supersede
        if (currentActive is not null && currentActive.Id != version.Id)
        {
            var trackedActive = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, currentActive.Id, ct);
            trackedActive?.Supersede(DateTimeOffset.UtcNow);
        }

        version.Approve(_tenant.UserId.ToString());
        version.Activate(DateTimeOffset.UtcNow);

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_ACTIVATED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            $"{{\"ruleId\":\"{version.RuleId}\",\"version\":{version.VersionNumber}}}",
            "Rule version activated",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    private static RuleSummaryResponse ToSummary(AmlRuleVersion v) => new(
        v.RuleId, v.Id, v.RuleCode, v.RuleName, v.VersionNumber, v.Status.ToString(),
        v.Definition.Severity.ToString(), v.Definition.RiskScore, v.EffectiveFrom);

    private static RuleDetailResponse ToDetail(AmlRuleVersion v) => new(
        v.RuleId, v.Id, v.RuleCode, v.RuleName, v.VersionNumber, v.Status.ToString(),
        v.Definition.Severity.ToString(), v.Definition.RiskScore, v.EffectiveFrom, v.Definition);
}
