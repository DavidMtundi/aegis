namespace Aegis.Api.Controllers;

using Aegis.Infrastructure.Auth;
using Aegis.Modules.Aml.Application;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/tenants")]
public sealed class DevTenantsController : ControllerBase
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly PasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;
    private readonly IStructuringRuleSeeder _structuringSeeder;
    private readonly IUnitOfWork _uow;

    public DevTenantsController(
        IHostEnvironment environment,
        IConfiguration configuration,
        ITenantRepository tenants,
        IUserRepository users,
        PasswordHasher passwordHasher,
        IAuditWriter audit,
        IStructuringRuleSeeder structuringSeeder,
        IUnitOfWork uow)
    {
        _environment = environment;
        _configuration = configuration;
        _tenants = tenants;
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _structuringSeeder = structuringSeeder;
        _uow = uow;
    }

    public sealed record BootstrapTenantRequest(
        string Name,
        string Slug,
        string AdminEmail,
        string AdminName,
        string AdminPassword);

    public sealed record BootstrapTenantResponse(Guid TenantId, string Slug, Guid AdminUserId);

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<BootstrapTenantResponse>> Bootstrap(
        [FromBody] BootstrapTenantRequest request,
        CancellationToken ct)
    {
        if (!IsDevBootstrapAllowed())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Slug) ||
            string.IsNullOrWhiteSpace(request.AdminEmail) ||
            string.IsNullOrWhiteSpace(request.AdminPassword))
        {
            return BadRequest("Slug, AdminEmail and AdminPassword are required.");
        }

        var existing = await _tenants.GetBySlugAsync(request.Slug, ct);
        if (existing is not null)
        {
            return Conflict("Tenant slug already exists.");
        }

        var tenant = Tenant.Create(
            request.Name,
            request.Slug,
            "dev",
            request.AdminEmail,
            new TenantSettings
            {
                DefaultCurrency = "KES",
                DefaultCountry = "KE",
                Timezone = "Africa/Nairobi",
                Locale = "en-KE"
            });
        tenant.Activate();
        await _tenants.AddAsync(tenant, ct);

        var tenantId = new TenantId(tenant.Id);
        var admin = Modules.Identity.Domain.User.Create(
            tenantId,
            request.AdminEmail,
            request.AdminName,
            _passwordHasher.Hash(request.AdminPassword),
            new[] { "Admin" });
        await _users.AddAsync(admin, ct);

        await _audit.AppendAsync(AuditEvent.Create(
            tenant.Id,
            AuditEventTypes.TENANT_CREATED,
            nameof(Tenant),
            tenant.Id.ToString(),
            "system",
            "bootstrap",
            null,
            $"{{\"slug\":\"{tenant.Slug}\"}}",
            "Dev bootstrap created tenant",
            HttpContext.TraceIdentifier), ct);

        await _audit.AppendAsync(AuditEvent.Create(
            tenant.Id,
            AuditEventTypes.USER_CREATED,
            nameof(Modules.Identity.Domain.User),
            admin.Id.ToString(),
            "system",
            "bootstrap",
            null,
            $"{{\"email\":\"{admin.Email}\"}}",
            "Dev bootstrap created admin user",
            HttpContext.TraceIdentifier), ct);

        await _structuringSeeder.EnsureSeededAsync(tenantId, ct);
        await _uow.SaveChangesAsync(ct);

        return Created($"/api/v1/tenants/{tenant.Id}", new BootstrapTenantResponse(tenant.Id, tenant.Slug, admin.Id));
    }

    private bool IsDevBootstrapAllowed()
        => _environment.IsDevelopment()
           || string.Equals(_configuration["Aegis:AllowDevBootstrap"], "true", StringComparison.OrdinalIgnoreCase);
}
