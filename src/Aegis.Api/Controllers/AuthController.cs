namespace Aegis.Api.Controllers;

using System.Security.Cryptography;
using System.Text;
using Aegis.Infrastructure.Auth;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly PasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IConfiguration _config;

    public AuthController(
        ITenantRepository tenants,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        PasswordHasher passwordHasher,
        IJwtTokenService jwt,
        IAuditWriter audit,
        IUnitOfWork uow,
        IConfiguration config)
    {
        _tenants = tenants;
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _audit = audit;
        _uow = uow;
        _config = config;
    }

    public sealed record LoginRequest(string Email, string Password, string TenantSlug);
    public sealed record LoginResponse(string AccessToken, string RefreshToken, Guid TenantId, Guid UserId, string Email);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string? RefreshToken);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var tenant = await _tenants.GetBySlugAsync(request.TenantSlug, ct);
        if (tenant is null || tenant.Status != TenantStatus.ACTIVE)
            return Unauthorized();

        var tenantId = new TenantId(tenant.Id);
        var user = await _users.GetByTenantAndEmailAsync(tenantId, request.Email, ct);
        if (user is null || user.Status != UserStatus.ACTIVE || !_passwordHasher.Verify(user.PasswordHash, request.Password))
            return Unauthorized();

        user.RecordLogin();
        await _users.UpdateAsync(user, ct);

        var refreshPlain = CreateRefreshTokenValue();
        var refreshEntity = RefreshToken.Issue(tenantId, user.Id, HashToken(refreshPlain), RefreshExpiry());
        await _refreshTokens.AddAsync(refreshEntity, ct);

        await _audit.AppendAsync(AuditEvent.Create(
            tenant.Id,
            AuditEventTypes.USER_LOGIN,
            nameof(User),
            user.Id.ToString(),
            user.Id.ToString(),
            user.RoleNames.FirstOrDefault(),
            null,
            null,
            "User logged in",
            HttpContext.TraceIdentifier), ct);

        await _uow.SaveChangesAsync(ct);

        var access = _jwt.CreateAccessToken(user, tenant);
        return Ok(new LoginResponse(access, refreshPlain, tenant.Id, user.Id, user.Email));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Unauthorized();

        var existing = await _refreshTokens.GetByHashAsync(HashToken(request.RefreshToken.Trim()), ct);
        if (existing is null || !existing.IsActive(DateTimeOffset.UtcNow))
            return Unauthorized();

        var tenant = await _tenants.GetByIdAsync(existing.TenantId.Value, ct);
        if (tenant is null || tenant.Status != TenantStatus.ACTIVE)
            return Unauthorized();

        var user = await _users.GetByTenantAndIdAsync(existing.TenantId, existing.UserId, ct);
        if (user is null || user.Status != UserStatus.ACTIVE)
            return Unauthorized();

        var newPlain = CreateRefreshTokenValue();
        var replacement = RefreshToken.Issue(existing.TenantId, user.Id, HashToken(newPlain), RefreshExpiry());
        existing.Revoke(replacement.Id);
        await _refreshTokens.UpdateAsync(existing, ct);
        await _refreshTokens.AddAsync(replacement, ct);
        await _uow.SaveChangesAsync(ct);

        return Ok(new LoginResponse(_jwt.CreateAccessToken(user, tenant), newPlain, tenant.Id, user.Id, user.Email));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            var existing = await _refreshTokens.GetByHashAsync(HashToken(request.RefreshToken.Trim()), ct);
            if (existing is not null && existing.RevokedAt is null)
            {
                existing.Revoke();
                await _refreshTokens.UpdateAsync(existing, ct);
                await _uow.SaveChangesAsync(ct);
            }
        }

        return NoContent();
    }

    private DateTimeOffset RefreshExpiry()
    {
        var days = _config.GetValue("Jwt:RefreshTokenDays", 14);
        return DateTimeOffset.UtcNow.AddDays(Math.Clamp(days, 1, 90));
    }

    private static string CreateRefreshTokenValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
