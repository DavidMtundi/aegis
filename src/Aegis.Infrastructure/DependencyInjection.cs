namespace Aegis.Infrastructure;

using System.Security.Claims;
using System.Text;
using Aegis.Infrastructure.Auth;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Persistence.Repositories;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Customers.Application;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Transactions.Application;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

public static class DependencyInjection
{
    public static IServiceCollection AddAegisInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AegisDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Aegis")));

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<TransactionRepository>();
        services.AddScoped<ITransactionRepository>(sp => sp.GetRequiredService<TransactionRepository>());
        services.AddScoped<ITransactionReadPort>(sp => sp.GetRequiredService<TransactionRepository>());
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<PasswordHasher>();

        services.AddHealthChecks()
            .AddDbContextCheck<AegisDbContext>();

        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey must be configured.");
        var issuer = configuration["Jwt:Issuer"] ?? "aegis";
        var audience = configuration["Jwt:Audience"] ?? "aegis-api";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = ClaimTypes.Role
                };
            });

        return services;
    }
}
