namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Application.Aml;
using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[EnableRateLimiting("api")]
[Authorize]
[Route("api/v1/rules")]
public sealed class RulesController : ControllerBase
{
    private readonly IAmlRuleRepository _rules;
    private readonly IAmlRuleVersionRepository _ruleVersions;
    private readonly RuleDefinitionValidator _validator;
    private readonly IRuleSimulator _simulator;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public RulesController(
        IAmlRuleRepository rules,
        IAmlRuleVersionRepository ruleVersions,
        RuleDefinitionValidator validator,
        IRuleSimulator simulator,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _rules = rules;
        _ruleVersions = ruleVersions;
        _validator = validator;
        _simulator = simulator;
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
        DateTimeOffset? EffectiveFrom,
        string CreatedBy);

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
        string CreatedBy,
        string? ApprovedBy,
        RuleDefinition Definition);

    public sealed record CreateDraftRequest(RuleDefinition Definition);

    public sealed record CreateRuleRequest(
        string Code,
        string Name,
        string? Description,
        ScenarioType ScenarioType,
        RuleDefinition Definition);

    public sealed record SimulateRequest(int LookbackDays = 30);

    public sealed record ValidateRuleRequest(RuleDefinition Definition);

    public sealed record ValidateRuleResponse(bool Valid, IReadOnlyList<string> Errors);

    public sealed record UpdateDraftRequest(RuleDefinition Definition);

    [HttpGet]
    [RequirePermission(Permissions.RuleRead)]
    public async Task<ActionResult<IReadOnlyList<RuleSummaryResponse>>> List(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var versions = await _ruleVersions.ListByTenantAsync(_tenant.TenantId, ct);
        var current = SelectCurrentVersions(versions);
        return Ok(current.OrderBy(v => v.RuleCode).Select(ToSummary).ToList());
    }

    [HttpGet("pending-approval")]
    [RequirePermission(Permissions.RuleApprove)]
    public async Task<ActionResult<IReadOnlyList<RuleSummaryResponse>>> ListPendingApproval(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var versions = await _ruleVersions.ListByTenantAsync(_tenant.TenantId, ct);
        var pending = versions
            .Where(v => v.Status == RuleVersionStatus.PENDING_APPROVAL)
            .OrderBy(v => v.RuleCode)
            .ThenByDescending(v => v.VersionNumber)
            .Select(ToSummary)
            .ToList();
        return Ok(pending);
    }

    [HttpPost("validate")]
    [RequirePermission(Permissions.RuleCreate)]
    public ActionResult<ValidateRuleResponse> Validate([FromBody] ValidateRuleRequest request)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (request.Definition is null) return BadRequest("Definition is required.");
        var validation = _validator.Validate(request.Definition);
        return Ok(new ValidateRuleResponse(validation.IsValid, validation.Errors));
    }

    [HttpPut("versions/{versionId:guid}")]
    [RequirePermission(Permissions.RuleCreate)]
    public async Task<ActionResult<RuleDetailResponse>> UpdateDraft(
        Guid versionId,
        [FromBody] UpdateDraftRequest request,
        CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (request.Definition is null) return BadRequest("Definition is required.");

        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        if (version is null) return NotFound();

        var validation = _validator.Validate(request.Definition);
        if (!validation.IsValid)
            return BadRequest(string.Join("; ", validation.Errors));

        try
        {
            version.UpdateDefinition(request.Definition);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_UPDATED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId = version.RuleId, version = version.VersionNumber }),
            "Draft rule version updated",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpGet("versions/{versionId:guid}")]
    [RequirePermission(Permissions.RuleRead)]
    public async Task<ActionResult<RuleDetailResponse>> GetVersion(Guid versionId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        return version is null ? NotFound() : Ok(ToDetail(version));
    }

    [HttpGet("{ruleId:guid}")]
    [RequirePermission(Permissions.RuleRead)]
    public async Task<ActionResult<RuleDetailResponse>> GetByRuleId(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var versions = await _ruleVersions.ListByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        var current = SelectCurrentVersions(versions).FirstOrDefault();
        return current is null ? NotFound() : Ok(ToDetail(current));
    }

    [HttpGet("{ruleId:guid}/versions")]
    [RequirePermission(Permissions.RuleRead)]
    public async Task<ActionResult<IReadOnlyList<RuleSummaryResponse>>> ListVersions(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var versions = await _ruleVersions.ListByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        return Ok(versions.Select(ToSummary).ToList());
    }

    [HttpPost]
    [RequirePermission(Permissions.RuleCreate)]
    public async Task<ActionResult<RuleDetailResponse>> Create([FromBody] CreateRuleRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (request.Definition is null) return BadRequest("Definition is required.");
        if (string.IsNullOrWhiteSpace(request.Code)) return BadRequest("Code is required.");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        var definition = request.Definition with
        {
            Code = string.IsNullOrWhiteSpace(request.Definition.Code) ? request.Code.Trim() : request.Definition.Code,
            Name = string.IsNullOrWhiteSpace(request.Definition.Name) ? request.Name.Trim() : request.Definition.Name
        };

        var validation = _validator.Validate(definition);
        if (!validation.IsValid)
            return BadRequest(string.Join("; ", validation.Errors));

        if (!string.Equals(definition.Code, request.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest("Definition code must match rule code.");

        var existing = await _rules.GetByTenantAndCodeAsync(_tenant.TenantId, request.Code.Trim(), ct);
        if (existing is not null)
            return Conflict($"A rule with code '{request.Code.Trim()}' already exists.");

        var createdBy = _tenant.UserId.ToString();
        var rule = AmlRule.CreateDraft(
            _tenant.TenantId,
            request.Code.Trim(),
            request.Name.Trim(),
            request.Description?.Trim() ?? string.Empty,
            request.ScenarioType,
            createdBy);

        var version = AmlRuleVersion.Create(rule.Id, _tenant.TenantId, 1, definition, createdBy);

        await _rules.AddAsync(rule, ct);
        await _ruleVersions.AddAsync(version, ct);
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_CREATED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            createdBy,
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId = rule.Id, code = rule.Code, version = 1 }),
            "Rule created as draft",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);

        return Created($"/api/v1/rules/{rule.Id}", ToDetail(version));
    }

    [HttpPost("{ruleId:guid}/versions")]
    [RequirePermission(Permissions.RuleCreate)]
    public async Task<ActionResult<RuleDetailResponse>> CreateDraft(Guid ruleId, [FromBody] CreateDraftRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (request.Definition is null) return BadRequest("Definition is required.");

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, ruleId, ct);
        if (rule is null) return NotFound();
        if (rule.Status == RuleStatus.RETIRED)
            return Conflict("Cannot create a new version of a retired rule.");

        var validation = _validator.Validate(request.Definition);
        if (!validation.IsValid)
            return BadRequest(string.Join("; ", validation.Errors));

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
            AuditPayload.Json(new { ruleId, version = next }),
            "Draft rule version created",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Created($"/api/v1/rules/{ruleId}/versions", ToDetail(draft));
    }

    [HttpPost("versions/{versionId:guid}/submit")]
    [RequirePermission(Permissions.RuleCreate)]
    public async Task<ActionResult<RuleDetailResponse>> Submit(Guid versionId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        if (version is null) return NotFound();

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, version.RuleId, ct);
        if (rule is null) return NotFound();
        if (rule.Status == RuleStatus.RETIRED)
            return Conflict("Cannot submit a version of a retired rule.");

        try
        {
            version.SubmitForApproval();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        // Only flip parent when this rule has never been live (new rule drafts).
        if (rule.Status == RuleStatus.DRAFT)
            rule.SubmitForApproval();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_SUBMITTED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId = version.RuleId, version = version.VersionNumber }),
            "Rule version submitted for approval",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpPost("versions/{versionId:guid}/approve")]
    [RequirePermission(Permissions.RuleApprove)]
    public async Task<ActionResult<RuleDetailResponse>> Approve(Guid versionId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        if (version is null) return NotFound();

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, version.RuleId, ct);
        if (rule is null) return NotFound();

        try
        {
            version.Approve(_tenant.UserId.ToString());
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        if (rule.Status == RuleStatus.PENDING_APPROVAL)
            rule.Approve();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_APPROVED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId = version.RuleId, version = version.VersionNumber }),
            "Rule version approved",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpPost("versions/{versionId:guid}/reject")]
    [RequirePermission(Permissions.RuleApprove)]
    public async Task<ActionResult<RuleDetailResponse>> Reject(Guid versionId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        if (version is null) return NotFound();

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, version.RuleId, ct);
        if (rule is null) return NotFound();

        try
        {
            version.ReturnToDraft();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        if (rule.Status is RuleStatus.PENDING_APPROVAL or RuleStatus.APPROVED)
            rule.ReturnToDraft();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_UPDATED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId = version.RuleId, version = version.VersionNumber, action = "reject" }),
            "Rule version rejected back to draft",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpPost("versions/{versionId:guid}/withdraw")]
    [RequirePermission(Permissions.RuleCreate)]
    public async Task<ActionResult<RuleDetailResponse>> Withdraw(Guid versionId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        if (version is null) return NotFound();

        if (!string.Equals(version.CreatedBy, _tenant.UserId.ToString(), StringComparison.Ordinal))
            return Forbid();

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, version.RuleId, ct);
        if (rule is null) return NotFound();

        try
        {
            version.ReturnToDraft();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        if (rule.Status is RuleStatus.PENDING_APPROVAL or RuleStatus.APPROVED)
            rule.ReturnToDraft();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_UPDATED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId = version.RuleId, version = version.VersionNumber, action = "withdraw" }),
            "Rule version withdrawn back to draft",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpPost("versions/{versionId:guid}/activate")]
    [RequirePermission(Permissions.RuleActivate)]
    public async Task<ActionResult<RuleDetailResponse>> Activate(Guid versionId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, versionId, ct);
        if (version is null) return NotFound();
        if (version.Status == RuleVersionStatus.ACTIVE)
            return Ok(ToDetail(version));
        if (version.Status == RuleVersionStatus.SUSPENDED)
            return Conflict("Use resume to restore a suspended version.");
        if (version.Status is not RuleVersionStatus.APPROVED)
            return Conflict($"Cannot activate a rule version in status {version.Status}. Submit and approve first.");

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, version.RuleId, ct);
        if (rule is null) return NotFound();
        if (rule.Status == RuleStatus.RETIRED)
            return Conflict("Cannot activate a version of a retired rule.");

        var currentActive = await _ruleVersions.GetActiveByTenantAndRuleIdAsync(_tenant.TenantId, version.RuleId, ct);
        if (currentActive is not null && currentActive.Id != version.Id)
        {
            var trackedActive = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, currentActive.Id, ct);
            trackedActive?.Supersede(DateTimeOffset.UtcNow);
        }

        var siblings = await _ruleVersions.ListByTenantAndRuleIdAsync(_tenant.TenantId, version.RuleId, ct);
        foreach (var sibling in siblings.Where(s => s.Status == RuleVersionStatus.SUSPENDED && s.Id != version.Id))
        {
            var tracked = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, sibling.Id, ct);
            tracked?.Retire(DateTimeOffset.UtcNow);
        }

        try
        {
            version.Activate(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        rule.Activate();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_ACTIVATED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId = version.RuleId, version = version.VersionNumber }),
            "Rule version activated",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpPost("versions/{versionId:guid}/simulate")]
    [RequirePermission(Permissions.RuleRead)]
    public async Task<ActionResult<RuleSimulationResult>> Simulate(
        Guid versionId,
        [FromBody] SimulateRequest? request,
        CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var lookbackDays = request?.LookbackDays ?? 30;
        try
        {
            var result = await _simulator.SimulateAsync(_tenant.TenantId, versionId, lookbackDays, ct);
            return Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPost("{ruleId:guid}/suspend")]
    [RequirePermission(Permissions.RuleActivate)]
    public async Task<ActionResult<RuleDetailResponse>> Suspend(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, ruleId, ct);
        if (rule is null) return NotFound();
        if (rule.Status == RuleStatus.RETIRED)
            return Conflict("Cannot suspend a retired rule.");

        var active = await _ruleVersions.GetActiveByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        if (active is null)
            return Conflict("No active version to suspend.");

        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, active.Id, ct);
        if (version is null) return NotFound();

        try
        {
            version.Suspend();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        rule.Disable();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_DISABLED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId, version = version.VersionNumber }),
            "Rule version suspended",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpPost("{ruleId:guid}/resume")]
    [RequirePermission(Permissions.RuleActivate)]
    public async Task<ActionResult<RuleDetailResponse>> Resume(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, ruleId, ct);
        if (rule is null) return NotFound();
        if (rule.Status == RuleStatus.RETIRED)
            return Conflict("Cannot resume a retired rule.");

        var versions = await _ruleVersions.ListByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        var suspended = versions.FirstOrDefault(v => v.Status == RuleVersionStatus.SUSPENDED);
        if (suspended is null)
            return Conflict("No suspended version to resume.");

        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, suspended.Id, ct);
        if (version is null) return NotFound();

        try
        {
            version.Resume();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        rule.Activate();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_ACTIVATED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId, version = version.VersionNumber }),
            "Rule version resumed",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    [HttpPost("{ruleId:guid}/retire")]
    [RequirePermission(Permissions.RuleActivate)]
    public async Task<ActionResult<RuleDetailResponse>> Retire(Guid ruleId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        var rule = await _rules.GetByTenantAndIdAsync(_tenant.TenantId, ruleId, ct);
        if (rule is null) return NotFound();

        var versions = await _ruleVersions.ListByTenantAndRuleIdAsync(_tenant.TenantId, ruleId, ct);
        var current = versions.FirstOrDefault(v => v.Status is RuleVersionStatus.ACTIVE or RuleVersionStatus.SUSPENDED)
                      ?? versions.FirstOrDefault(v => v.Status == RuleVersionStatus.RETIRED);
        if (current is null)
            return Conflict("No version available to retire.");

        if (current.Status == RuleVersionStatus.RETIRED)
        {
            if (rule.Status != RuleStatus.RETIRED) rule.Retire();
            await _uow.SaveChangesAsync(ct);
            return Ok(ToDetail(current));
        }

        var version = await _ruleVersions.GetByTenantAndIdAsync(_tenant.TenantId, current.Id, ct);
        if (version is null) return NotFound();

        try
        {
            version.Retire(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        rule.Retire();

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.RULE_RETIRED,
            nameof(AmlRuleVersion),
            version.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            AuditPayload.Json(new { ruleId, version = version.VersionNumber }),
            "Rule version retired",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDetail(version));
    }

    /// <summary>
    /// Prefer ACTIVE, then SUSPENDED, else highest version number that is not SUPERSEDED.
    /// </summary>
    internal static IReadOnlyList<AmlRuleVersion> SelectCurrentVersions(IEnumerable<AmlRuleVersion> versions) =>
        versions
            .GroupBy(v => v.RuleId)
            .Select(g =>
                g.FirstOrDefault(v => v.Status == RuleVersionStatus.ACTIVE)
                ?? g.FirstOrDefault(v => v.Status == RuleVersionStatus.SUSPENDED)
                ?? g.Where(v => v.Status != RuleVersionStatus.SUPERSEDED)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault())
            .Where(v => v is not null)
            .Cast<AmlRuleVersion>()
            .ToList();

    private static RuleSummaryResponse ToSummary(AmlRuleVersion v) => new(
        v.RuleId, v.Id, v.RuleCode, v.RuleName, v.VersionNumber, v.Status.ToString(),
        v.Definition.Severity.ToString(), v.Definition.RiskScore, v.EffectiveFrom, v.CreatedBy);

    private static RuleDetailResponse ToDetail(AmlRuleVersion v) => new(
        v.RuleId, v.Id, v.RuleCode, v.RuleName, v.VersionNumber, v.Status.ToString(),
        v.Definition.Severity.ToString(), v.Definition.RiskScore, v.EffectiveFrom,
        v.CreatedBy, v.ApprovedBy, v.Definition);
}
