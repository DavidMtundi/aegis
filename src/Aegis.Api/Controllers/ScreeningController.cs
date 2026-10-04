namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Application.Risk;
using Aegis.Application.Screening;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class ScreeningController : ControllerBase
{
    private const long MaxListBytes = 5 * 1024 * 1024;

    /// <summary>Refresh failures can carry upstream URLs or parser internals; the detail stays in the server log.</summary>
    private const string RefreshFailedMessage = "Download or parse failed; see server logs.";

    private readonly ICustomerScreeningService _screening;
    private readonly IScreeningMatchRepository _matches;
    private readonly IWatchlistRepository _lists;
    private readonly ITenantWatchlistRepository _tenantLists;
    private readonly IWatchlistRefresher _refresher;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;

    public ScreeningController(
        ICustomerScreeningService screening,
        IScreeningMatchRepository matches,
        IWatchlistRepository lists,
        ITenantWatchlistRepository tenantLists,
        IWatchlistRefresher refresher,
        IAuditWriter audit,
        IUnitOfWork uow,
        ITenantContext tenant)
    {
        _screening = screening;
        _matches = matches;
        _lists = lists;
        _tenantLists = tenantLists;
        _refresher = refresher;
        _audit = audit;
        _uow = uow;
        _tenant = tenant;
    }

    public sealed record ReviewRequest(string? Reason);

    public sealed record MatchSummaryDto(
        Guid Id, Guid CustomerId, string CustomerName, string Source, string Category, string MatchedName,
        double Score, bool BirthYearMatched, string Status, string Trigger, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);

    public sealed record SubjectDto(
        string Source, string ExternalId, string Category, string Kind, IReadOnlyList<string> Names,
        IReadOnlyList<string> BirthYears, IReadOnlyList<string> Countries, string? Detail);

    public sealed record MatchDetailDto(MatchSummaryDto Match, SubjectDto Subject, string? ReviewedBy, string? ReviewReason, Guid? AlertId);

    public sealed record ListStatusDto(string Source, DateTimeOffset LoadedAt, DateTimeOffset? PublishedAt, int EntryCount);

    public sealed record ListsResponse(IReadOnlyList<ListStatusDto> Sources, TenantWatchlistSummary TenantList);

    public sealed record RefreshResultDto(string Source, string Status, int EntryCount, string? Error);

    public sealed record RefreshResponse(IReadOnlyList<RefreshResultDto> Results, int NewMatches);

    public sealed record UploadResponse(int EntryCount, int NewMatches);

    public sealed record ScreenResponse(int NewMatches, IReadOnlyList<Guid> MatchIds);

    [HttpGet("screening/lists")]
    [RequirePermission(Permissions.ScreeningRead)]
    public async Task<ActionResult<ListsResponse>> Lists(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var loads = await _lists.ListLatestLoadsAsync(ct);
        return new ListsResponse(
            loads.Select(l => new ListStatusDto(l.Source.ToString(), l.LoadedAt, l.PublishedAt, l.EntryCount)).ToList(),
            await _tenantLists.GetSummaryAsync(_tenant.TenantId, ct));
    }

    /// <summary>Refreshes the shared lists now and rescreens this tenant; other tenants are rescreened by the daily job.</summary>
    [HttpPost("screening/lists/refresh")]
    [RequirePermission(Permissions.ScreeningManage)]
    public async Task<ActionResult<RefreshResponse>> Refresh(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var results = new List<WatchlistRefreshResult>();
        foreach (var source in _refresher.Sources) results.Add(await _refresher.RefreshAsync(source, ct));

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value, AuditEventTypes.SCREENING_LIST_REFRESHED, "GlobalWatchlist",
            string.Join(",", results.Select(r => r.Source)), _tenant.UserId.ToString(), _tenant.Roles.FirstOrDefault(), null,
            AuditPayload.Json(new
            {
                results = results.Select(r => new { source = r.Source.ToString(), status = r.Status.ToString(), entryCount = r.EntryCount })
            }),
            "Screening lists refreshed: " + string.Join(", ", results.Select(r => $"{r.Source} {r.Status}")),
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);

        var newMatches = 0;
        if (results.Any(r => r.Status == WatchlistRefreshStatus.LOADED))
            newMatches = (await _screening.ScreenTenantAsync(_tenant.TenantId, ScreeningTriggers.ListUpdated, Actor(), ct)).NewMatchIds.Count;

        return new RefreshResponse(
            results.Select(r => new RefreshResultDto(
                r.Source.ToString(), r.Status.ToString(), r.EntryCount,
                r.Status == WatchlistRefreshStatus.FAILED ? RefreshFailedMessage : null)).ToList(),
            newMatches);
    }

    [HttpPut("screening/tenant-list")]
    [RequirePermission(Permissions.ScreeningManage)]
    [RequestSizeLimit(MaxListBytes + 64 * 1024)]
    public async Task<IActionResult> UploadTenantList(IFormFile? file, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest(new { errors = new[] { "Attach a CSV file as 'file'." } });
        if (file.Length > MaxListBytes) return BadRequest(new { errors = new[] { "The file is larger than 5 MB." } });

        string csv;
        using (var reader = new StreamReader(file.OpenReadStream()))
            csv = await reader.ReadToEndAsync(ct);

        var parsed = TenantWatchlistCsvParser.Parse(csv);
        if (parsed.Errors.Count > 0) return BadRequest(new { errors = parsed.Errors });

        var uploadedBy = _tenant.UserId.ToString();
        await _tenantLists.ReplaceAsync(
            _tenant.TenantId,
            parsed.Subjects.Select(s => TenantWatchlistEntry.From(_tenant.TenantId, s, uploadedBy)).ToList(),
            ct);
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value, AuditEventTypes.SCREENING_LIST_UPLOADED, "TenantWatchlist", _tenant.TenantId.Value.ToString(),
            uploadedBy, _tenant.Roles.FirstOrDefault(), null,
            AuditPayload.Json(new { entryCount = parsed.Subjects.Count, fileName = file.FileName }),
            $"Screening list replaced with {parsed.Subjects.Count} entries", HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);

        var outcome = await _screening.ScreenTenantAsync(_tenant.TenantId, ScreeningTriggers.ListUpdated, Actor(), ct);
        return Ok(new UploadResponse(parsed.Subjects.Count, outcome.NewMatchIds.Count));
    }

    [HttpGet("screening/matches")]
    [RequirePermission(Permissions.ScreeningRead)]
    public async Task<IActionResult> ListMatches(
        [FromQuery] string? status, [FromQuery] Guid? customerId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        ScreeningMatchStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ScreeningMatchStatus>(status, true, out var s) || !Enum.IsDefined(s))
                return BadRequest("Status must be PENDING_REVIEW, CONFIRMED or FALSE_POSITIVE.");
            parsedStatus = s;
        }

        var result = await _matches.ListAsync(_tenant.TenantId, new ScreeningMatchQuery(parsedStatus, customerId, page, pageSize), ct);
        return Ok(new
        {
            items = result.Items.Select(ToSummary).ToList(),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount
        });
    }

    [HttpGet("screening/matches/{id:guid}")]
    [RequirePermission(Permissions.ScreeningRead)]
    public async Task<ActionResult<MatchDetailDto>> GetMatch(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var match = await _matches.GetAsync(_tenant.TenantId, id, ct);
        return match is null ? NotFound() : ToDetail(match);
    }

    [HttpPost("screening/matches/{id:guid}/confirm")]
    [RequirePermission(Permissions.ScreeningReview)]
    public Task<IActionResult> Confirm(Guid id, [FromBody] ReviewRequest request, CancellationToken ct)
        => ReviewAsync(id, ScreeningDecision.CONFIRM, request, ct);

    [HttpPost("screening/matches/{id:guid}/dismiss")]
    [RequirePermission(Permissions.ScreeningReview)]
    public Task<IActionResult> Dismiss(Guid id, [FromBody] ReviewRequest request, CancellationToken ct)
        => ReviewAsync(id, ScreeningDecision.FALSE_POSITIVE, request, ct);

    [HttpPost("customers/{id:guid}/screen")]
    [RequirePermission(Permissions.CustomerWrite)]
    public async Task<ActionResult<ScreenResponse>> ScreenCustomer(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var outcome = await _screening.ScreenCustomerAsync(_tenant.TenantId, id, ScreeningTriggers.Manual, Actor(), ct);
        return outcome is null ? NotFound() : new ScreenResponse(outcome.NewMatchIds.Count, outcome.NewMatchIds);
    }

    private async Task<IActionResult> ReviewAsync(Guid id, ScreeningDecision decision, ReviewRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest("A reason is required.");
        if (request.Reason.Trim().Length > ScreeningLimits.ReviewReasonMaxLength)
            return BadRequest($"The reason is longer than {ScreeningLimits.ReviewReasonMaxLength} characters.");
        try
        {
            var match = await _screening.ReviewAsync(_tenant.TenantId, id, decision, request.Reason, Actor(), ct);
            return match is null ? NotFound() : Ok(ToSummary(match));
        }
        catch (ScreeningMatchAlreadyReviewedException ex)
        {
            return Conflict(ex.Message);
        }
        catch (UniqueConstraintViolationException)
        {
            return Conflict("This match was reviewed by someone else. Reload to see the decision.");
        }
    }

    private RiskActor Actor() => new(_tenant.UserId.ToString(), _tenant.Roles.FirstOrDefault(), HttpContext.TraceIdentifier);

    private static MatchSummaryDto ToSummary(ScreeningMatch m) => new(
        m.Id, m.CustomerId, m.CustomerName, m.Source.ToString(), m.Category.ToString(), m.MatchedName,
        m.Score, m.BirthYearMatched, m.Status.ToString(), m.Trigger, m.CreatedAt, m.ReviewedAt);

    private static MatchDetailDto ToDetail(ScreeningMatch m) => new(
        ToSummary(m),
        new SubjectDto(
            m.Subject.Source.ToString(), m.Subject.ExternalId, m.Subject.Category.ToString(), m.Subject.Kind.ToString(),
            m.Subject.Names,
            m.Subject.BirthYears.Select(r => r.From == r.To ? $"{r.From}" : $"{r.From}–{r.To}").ToList(),
            m.Subject.Countries, m.Subject.Detail),
        m.ReviewedBy, m.ReviewReason, m.AlertId);
}
