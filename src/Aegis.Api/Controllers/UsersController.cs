namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Infrastructure.Auth;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainUser = Aegis.Modules.Identity.Domain.User;

[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    private const int MinPasswordLength = 8;

    private readonly IUserRepository _users;
    private readonly PasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;

    public UsersController(
        IUserRepository users,
        PasswordHasher passwordHasher,
        IAuditWriter audit,
        IUnitOfWork uow,
        ITenantContext tenant)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _uow = uow;
        _tenant = tenant;
    }

    public sealed record UserResponse(
        Guid Id,
        string Email,
        string Name,
        string Status,
        IReadOnlyList<string> Roles,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset CreatedAt);

    public sealed record AssigneeResponse(Guid Id, string Name, string Email);

    public sealed record CreateUserRequest(string Email, string Name, string Password, string[]? Roles);
    public sealed record SetRolesRequest(string[]? Roles);

    [HttpGet]
    [RequirePermission(Permissions.UserManage)]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> List(CancellationToken ct)
    {
        var users = await _users.ListByTenantAsync(_tenant.TenantId, ct);
        return Ok(users.Select(ToResponse).ToList());
    }

    /// <summary>Active users who can work cases; deliberately omits roles and login history.</summary>
    [HttpGet("assignees")]
    [RequirePermission(Permissions.CaseUpdate)]
    public async Task<ActionResult<IReadOnlyList<AssigneeResponse>>> Assignees(CancellationToken ct)
    {
        var users = await _users.ListByTenantAsync(_tenant.TenantId, ct);
        return Ok(users
            .Where(u => u.Status == UserStatus.ACTIVE && RolePermissions.Grants(u.RoleNames, Permissions.CaseUpdate))
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .Select(u => new AssigneeResponse(u.Id, u.Name, u.Email))
            .ToList());
    }

    [HttpPost]
    [RequirePermission(Permissions.UserManage)]
    public async Task<ActionResult<UserResponse>> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return BadRequest("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinPasswordLength)
            return BadRequest($"Password must be at least {MinPasswordLength} characters.");

        IReadOnlyList<string> roles;
        try
        {
            roles = DomainUser.ValidateRoles(request.Roles ?? Array.Empty<string>());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        if (await _users.GetByTenantAndEmailAsync(_tenant.TenantId, request.Email, ct) is not null)
            return Conflict("A user with this email already exists.");

        var user = DomainUser.Create(_tenant.TenantId, request.Email, request.Name, _passwordHasher.Hash(request.Password), roles);
        await _users.AddAsync(user, ct);
        await AppendAuditAsync(AuditEventTypes.USER_CREATED, user, AuditPayload.Json(new { email = user.Email, roles }), "User created", ct);
        await _uow.SaveChangesAsync(ct);

        return Created($"/api/v1/users/{user.Id}", ToResponse(user));
    }

    [HttpPut("{id:guid}/roles")]
    [RequirePermission(Permissions.UserManage)]
    public async Task<ActionResult<UserResponse>> SetRoles(Guid id, [FromBody] SetRolesRequest request, CancellationToken ct)
    {
        var user = await _users.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (user is null) return NotFound();

        IReadOnlyList<string> roles;
        try
        {
            roles = DomainUser.ValidateRoles(request.Roles ?? Array.Empty<string>());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        if (user.Id == _tenant.UserId && !roles.Contains(RoleNames.Admin))
            return BadRequest("You cannot remove your own Admin role.");

        var before = AuditPayload.Json(new { roles = user.RoleNames });
        user.SetRoles(roles);
        await _users.UpdateAsync(user, ct);
        await AppendAuditAsync(AuditEventTypes.USER_ROLES_CHANGED, user, AuditPayload.Json(new { roles }), "User roles changed", ct, before);
        await _uow.SaveChangesAsync(ct);

        return Ok(ToResponse(user));
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(Permissions.UserManage)]
    public async Task<ActionResult<UserResponse>> Deactivate(Guid id, CancellationToken ct)
    {
        var user = await _users.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (user is null) return NotFound();
        if (user.Id == _tenant.UserId) return BadRequest("You cannot deactivate yourself.");

        user.Deactivate();
        await _users.UpdateAsync(user, ct);
        await AppendAuditAsync(AuditEventTypes.USER_DEACTIVATED, user, null, "User deactivated", ct);
        await _uow.SaveChangesAsync(ct);

        return Ok(ToResponse(user));
    }

    private Task AppendAuditAsync(string eventType, DomainUser user, string? after, string reason, CancellationToken ct, string? before = null)
        => _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            eventType,
            nameof(Aegis.Modules.Identity.Domain.User),
            user.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            before,
            after,
            reason,
            HttpContext.TraceIdentifier), ct);

    private static UserResponse ToResponse(DomainUser u) => new(
        u.Id, u.Email, u.Name, u.Status.ToString(), u.RoleNames, u.LastLoginAt, u.CreatedAt);
}
