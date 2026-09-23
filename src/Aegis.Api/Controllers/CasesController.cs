namespace Aegis.Api.Controllers;

using Aegis.Modules.Alerts.Application;
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
[Route("api/v1/cases")]
public sealed class CasesController : ControllerBase
{
    private readonly ICaseRepository _cases;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public CasesController(
        ICaseRepository cases,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _cases = cases;
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
        IReadOnlyList<CaseNoteResponse> Notes);

    public sealed record CaseListResponse(
        IReadOnlyList<CaseResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    public sealed record AssignCaseRequest(string? AssignedTo);
    public sealed record AddNoteRequest(string Text);
    public sealed record CloseCaseRequest(string Disposition, string Conclusion);

    [HttpGet]
    public async Task<ActionResult<CaseListResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var result = await _cases.ListByTenantAsync(_tenant.TenantId, new CaseListQuery(page, pageSize), ct);
        return Ok(new CaseListResponse(
            result.Items.Select(ToResponse).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CaseResponse>> Get(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return c is null ? NotFound() : Ok(ToResponse(c));
    }

    [HttpPost("{id:guid}/assign")]
    public async Task<ActionResult<CaseResponse>> Assign(Guid id, [FromBody] AssignCaseRequest? request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();

        var assignee = string.IsNullOrWhiteSpace(request?.AssignedTo)
            ? _tenant.UserId.ToString()
            : request.AssignedTo.Trim();
        try
        {
            c.Assign(assignee);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.CASE_ASSIGNED,
            nameof(ComplianceCase),
            c.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            $"{{\"assignedTo\":\"{assignee}\"}}",
            "Case assigned",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToResponse(c));
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<ActionResult<CaseResponse>> AddNote(Guid id, [FromBody] AddNoteRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Text is required.");

        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();

        try
        {
            c.AddNote(request.Text, _tenant.UserId.ToString());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.CASE_UPDATED,
            nameof(ComplianceCase),
            c.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            null,
            "Case note added",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToResponse(c));
    }

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<CaseResponse>> Close(Guid id, [FromBody] CloseCaseRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Conclusion))
            return BadRequest("Conclusion is required.");
        if (!Enum.TryParse<CaseDisposition>(request.Disposition, true, out var disposition))
            return BadRequest("Invalid disposition.");

        var c = await _cases.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (c is null) return NotFound();

        try
        {
            c.Close(disposition, request.Conclusion);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.CASE_CLOSED,
            nameof(ComplianceCase),
            c.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            $"{{\"disposition\":\"{disposition}\"}}",
            request.Conclusion.Trim(),
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToResponse(c));
    }

    internal static CaseResponse ToResponse(ComplianceCase c) => new(
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
        c.Notes.Select(n => new CaseNoteResponse(n.Id, n.Text, n.AuthorId, n.CreatedAt)).ToList());
}
