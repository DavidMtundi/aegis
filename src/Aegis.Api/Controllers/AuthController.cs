namespace Aegis.Api.Controllers;

using Aegis.Infrastructure.Auth;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly PasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public AuthController(
        ITenantRepository tenants,
        IUserRepository users,
        PasswordHasher passwordHasher,
        IJwtTokenService jwt,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _tenants = tenants;
        _users = users;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _audit = audit;
        _uow = uow;
    }

    public sealed record LoginRequest(string Email, string Password, string TenantSlug);
    public sealed record LoginResponse(string AccessToken, Guid TenantId, Guid UserId, string Email);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var tenant = await _tenants.GetBySlugAsync(request.TenantSlug, ct);
        if (tenant is null)
        {
            return Unauthorized();
        }

        var tenantId = new TenantId(tenant.Id);
        var user = await _users.GetByTenantAndEmailAsync(tenantId, request.Email, ct);
        if (user is null || user.Status != UserStatus.ACTIVE || !_passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            return Unauthorized();
        }

        user.RecordLogin();
        await _users.UpdateAsync(user, ct);

        await _audit.AppendAsync(AuditEvent.Create(
            tenant.Id,
            AuditEventTypes.USER_LOGIN,
            nameof(Modules.Identity.Domain.User),
            user.Id.ToString(),
            user.Id.ToString(),
            user.RoleNames.FirstOrDefault(),
            null,
            null,
            "User logged in",
            HttpContext.TraceIdentifier), ct);

        await _uow.SaveChangesAsync(ct);

        var token = _jwt.CreateAccessToken(user, tenant);
        return Ok(new LoginResponse(token, tenant.Id, user.Id, user.Email));
    }
}
