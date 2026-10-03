namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Application.Risk;
using Aegis.Modules.Risk.Application;
using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class RiskController : ControllerBase
{
    private const int HistoryTake = 20;

    private readonly ICustomerRiskService _risk;
    private readonly IRiskModelRepository _models;
    private readonly ICustomerRiskScoreRepository _scores;
    private readonly ITenantContext _tenant;

    public RiskController(
        ICustomerRiskService risk,
        IRiskModelRepository models,
        ICustomerRiskScoreRepository scores,
        ITenantContext tenant)
    {
        _risk = risk;
        _models = models;
        _scores = scores;
        _tenant = tenant;
    }

    public sealed record RiskFactorDto(
        string Type,
        int Weight,
        IReadOnlyList<string>? Countries,
        IReadOnlyList<string>? CustomerTypes,
        int? PointsEach,
        int? Threshold,
        int? WindowDays);

    public sealed record RiskBandsDto(int Medium, int High, int Critical);

    public sealed record UpdateRiskModelRequest(IReadOnlyList<RiskFactorDto>? Factors, RiskBandsDto? Bands);

    public sealed record RiskModelVersionDto(int Version, bool IsActive, string CreatedBy, DateTimeOffset CreatedAt);

    public sealed record RiskModelResponse(
        Guid Id,
        int Version,
        string CreatedBy,
        DateTimeOffset CreatedAt,
        IReadOnlyList<RiskFactorDto> Factors,
        RiskBandsDto Bands,
        IReadOnlyList<RiskModelVersionDto> Versions);

    public sealed record RiskContributionDto(string Type, int Points, int Weight, string Detail);

    public sealed record RiskScoreDto(
        Guid Id,
        int Score,
        string Band,
        int ModelVersion,
        string Trigger,
        string CalculatedBy,
        DateTimeOffset CalculatedAt,
        IReadOnlyList<RiskContributionDto> Contributions);

    public sealed record CustomerRiskResponse(RiskScoreDto? Current, IReadOnlyList<RiskScoreDto> History);

    [HttpGet("risk/model")]
    [RequirePermission(Permissions.CustomerRead)]
    public async Task<ActionResult<RiskModelResponse>> GetModel(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var model = await _risk.GetOrCreateActiveModelAsync(_tenant.TenantId, Actor(), ct);
        return await ModelResponseAsync(model, ct);
    }

    [HttpPut("risk/model")]
    [RequirePermission(Permissions.RiskManage)]
    public async Task<ActionResult<RiskModelResponse>> UpdateModel([FromBody] UpdateRiskModelRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (request.Factors is null || request.Bands is null) return BadRequest("Factors and bands are required.");

        var factors = new List<RiskFactorDefinition>();
        foreach (var f in request.Factors)
        {
            if (!Enum.TryParse<RiskFactorType>(f.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
                return BadRequest($"Unknown factor type '{f.Type}'.");
            factors.Add(new RiskFactorDefinition(type, f.Weight, f.Countries, f.CustomerTypes, f.PointsEach, f.Threshold, f.WindowDays));
        }

        var bands = new RiskBands(request.Bands.Medium, request.Bands.High, request.Bands.Critical);
        var errors = RiskModelValidator.Validate(factors, bands);
        if (errors.Count > 0) return BadRequest(string.Join("; ", errors));

        try
        {
            var next = await _risk.UpdateModelAsync(_tenant.TenantId, factors, bands, Actor(), ct);
            return await ModelResponseAsync(next, ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return Conflict("The risk model was changed by someone else. Reload and try again.");
        }
    }

    public sealed record RecalculateAllResponse(int Recalculated);

    [HttpPost("risk/recalculate-all")]
    [RequirePermission(Permissions.RiskManage)]
    public async Task<ActionResult<RecalculateAllResponse>> RecalculateAll(
        [FromServices] IRiskBatchRecalculator batch, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        return new RecalculateAllResponse(await batch.RecalculateTenantAsync(_tenant.TenantId, Actor(), ct));
    }

    [HttpGet("customers/{id:guid}/risk")]
    [RequirePermission(Permissions.CustomerRead)]
    public async Task<ActionResult<CustomerRiskResponse>> GetCustomerRisk(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var history = await _scores.ListByCustomerAsync(_tenant.TenantId, id, HistoryTake, ct);
        if (history.Count == 0)
        {
            // Customers created before risk scoring existed have no history yet; score them on first view.
            var first = await _risk.RecalculateAsync(_tenant.TenantId, id, RiskTriggers.Manual, Actor(), ct);
            if (first is null) return NotFound();
            history = new[] { first };
        }

        var items = history.Select(ToDto).ToList();
        return new CustomerRiskResponse(items[0], items);
    }

    [HttpPost("customers/{id:guid}/risk/recalculate")]
    [RequirePermission(Permissions.CustomerWrite)]
    public async Task<ActionResult<RiskScoreDto>> Recalculate(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var score = await _risk.RecalculateAsync(_tenant.TenantId, id, RiskTriggers.Manual, Actor(), ct);
        return score is null ? NotFound() : ToDto(score);
    }

    private RiskActor Actor()
        => new(_tenant.UserId.ToString(), _tenant.Roles.FirstOrDefault(), HttpContext.TraceIdentifier);

    private async Task<RiskModelResponse> ModelResponseAsync(RiskModel model, CancellationToken ct)
    {
        var versions = await _models.ListVersionsAsync(_tenant.TenantId, ct);
        return new RiskModelResponse(
            model.Id,
            model.Version,
            model.CreatedBy,
            model.CreatedAt,
            model.Factors.Select(f => new RiskFactorDto(
                f.Type.ToString(), f.Weight, f.Countries, f.CustomerTypes, f.PointsEach, f.Threshold, f.WindowDays)).ToList(),
            new RiskBandsDto(model.Bands.Medium, model.Bands.High, model.Bands.Critical),
            versions.Select(v => new RiskModelVersionDto(v.Version, v.IsActive, v.CreatedBy, v.CreatedAt)).ToList());
    }

    private static RiskScoreDto ToDto(CustomerRiskScore s) => new(
        s.Id,
        s.Score,
        s.Band.ToString(),
        s.ModelVersion,
        s.Trigger,
        s.CalculatedBy,
        s.CalculatedAt,
        s.Contributions.Select(c => new RiskContributionDto(c.Type.ToString(), c.Points, c.Weight, c.Detail)).ToList());
}
