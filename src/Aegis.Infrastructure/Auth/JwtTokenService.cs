namespace Aegis.Infrastructure.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aegis.Modules.Identity.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

public interface IJwtTokenService
{
    string CreateAccessToken(User user, Tenant tenant);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration) => _configuration = configuration;

    public string CreateAccessToken(User user, Tenant tenant)
    {
        var signingKey = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? "aegis";
        var audience = _configuration["Jwt:Audience"] ?? "aegis-api";
        var lifetimeMinutes = int.TryParse(_configuration["Jwt:AccessTokenMinutes"], out var m) ? m : 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("tenant_id", tenant.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.Name)
        };

        foreach (var role in user.RoleNames)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(lifetimeMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class PasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(new object(), password);

    public bool Verify(string hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(new object(), hash, password);
        return result is Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success
            or Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded;
    }
}
