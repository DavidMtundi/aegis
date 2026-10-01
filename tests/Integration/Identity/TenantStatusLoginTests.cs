namespace Aegis.Tests.Integration.Identity;

using System.Net;
using System.Net.Http.Json;
using Aegis.Modules.Identity.Application;
using Aegis.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;

public sealed class TenantStatusLoginTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _slug = null!;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _client = _factory.CreateClient();
        _slug = $"susp-{Guid.NewGuid():N}"[..16];
        var bootstrap = await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Suspended Tenant",
            slug = _slug,
            adminEmail = $"admin@{_slug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        });
        bootstrap.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Suspended_tenant_users_cannot_log_in()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var tenant = (await tenants.GetBySlugAsync(_slug))!;
            tenant.Suspend();
            await tenants.UpdateAsync(tenant);
            await uow.SaveChangesAsync();
        }

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"admin@{_slug}.test",
            password = "Passw0rd!",
            tenantSlug = _slug
        });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
