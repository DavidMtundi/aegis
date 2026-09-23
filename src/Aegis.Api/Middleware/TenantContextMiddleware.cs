namespace Aegis.Api.Middleware;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Aegis.Shared.Domain;
using Aegis.Shared.Security;

public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenantContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var sub = httpContext.User.FindFirstValue("sub")
                ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var tenantIdValue = httpContext.User.FindFirstValue("tenant_id");

            if (Guid.TryParse(sub, out var userId) && Guid.TryParse(tenantIdValue, out var tenantId))
            {
                tenantContext.IsAuthenticated = true;
                tenantContext.UserId = userId;
                tenantContext.TenantId = new TenantId(tenantId);
                tenantContext.Roles = httpContext.User.FindAll(ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToArray();
            }
        }

        await _next(httpContext);
    }
}
