namespace Aegis.Tests.Integration.Identity;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

public sealed class AegisApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public AegisApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Aegis"] = _connectionString,
                ["Jwt:SigningKey"] = "dev-only-signing-key-change-me-32chars-min!!",
                ["Jwt:Issuer"] = "aegis",
                ["Jwt:Audience"] = "aegis-api",
                ["Aegis:AllowDevBootstrap"] = "true"
            });
        });
    }
}

public sealed class AuthAndAuditTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await EnsureDatabaseAsync();
        _factory = new AegisApiFactory(ConnectionString);
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Bootstrap_login_audit_and_tenant_isolation()
    {
        var slugA = $"tenant-a-{Guid.NewGuid():N}".Substring(0, 20);
        var slugB = $"tenant-b-{Guid.NewGuid():N}".Substring(0, 20);

        var bootstrapA = await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Tenant A",
            slug = slugA,
            adminEmail = "admin-a@example.com",
            adminName = "Admin A",
            adminPassword = "Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, bootstrapA.StatusCode);

        var bootstrapB = await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Tenant B",
            slug = slugB,
            adminEmail = "admin-b@example.com",
            adminName = "Admin B",
            adminPassword = "Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, bootstrapB.StatusCode);

        var loginA = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin-a@example.com",
            password = "Passw0rd!",
            tenantSlug = slugA
        });
        Assert.Equal(HttpStatusCode.OK, loginA.StatusCode);
        var loginABody = await loginA.Content.ReadFromJsonAsync<JsonElement>();
        var tokenA = loginABody.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(tokenA));

        var loginB = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin-b@example.com",
            password = "Passw0rd!",
            tenantSlug = slugB
        });
        Assert.Equal(HttpStatusCode.OK, loginB.StatusCode);
        var loginBBody = await loginB.Content.ReadFromJsonAsync<JsonElement>();
        var tokenB = loginBBody.GetProperty("accessToken").GetString();

        using var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var createAudit = await clientA.PostAsJsonAsync("/api/v1/audit-events", new
        {
            eventType = "SMOKE_TEST",
            entityType = "Smoke",
            entityId = Guid.NewGuid().ToString(),
            reason = "integration"
        });
        Assert.Equal(HttpStatusCode.Created, createAudit.StatusCode);
        var auditBody = await createAudit.Content.ReadFromJsonAsync<JsonElement>();
        var auditId = auditBody.GetProperty("id").GetGuid();

        var getOwn = await clientA.GetAsync($"/api/v1/audit-events/{auditId}");
        Assert.Equal(HttpStatusCode.OK, getOwn.StatusCode);

        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var getCrossTenant = await clientB.GetAsync($"/api/v1/audit-events/{auditId}");
        Assert.Equal(HttpStatusCode.NotFound, getCrossTenant.StatusCode);

        var status = await clientA.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusBody = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(statusBody.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Tenant_bootstrap_is_blocked_when_not_allowed()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Aegis"] = ConnectionString,
                        ["Jwt:SigningKey"] = "dev-only-signing-key-change-me-32chars-min!!",
                        ["Aegis:AllowDevBootstrap"] = "false"
                    });
                });
            });

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Blocked",
            slug = $"blocked-{Guid.NewGuid():N}".Substring(0, 20),
            adminEmail = "blocked@example.com",
            adminName = "Blocked",
            adminPassword = "Passw0rd!"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task EnsureDatabaseAsync()
    {
        // Create test database if missing using maintenance connection.
        var adminCs = "Host=localhost;Port=5432;Database=aegis;Username=aegis;Password=aegis_dev_password";
        await using var conn = new Npgsql.NpgsqlConnection(adminCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = 'aegis_test'";
        var exists = await cmd.ExecuteScalarAsync();
        if (exists is null)
        {
            cmd.CommandText = "CREATE DATABASE aegis_test";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
