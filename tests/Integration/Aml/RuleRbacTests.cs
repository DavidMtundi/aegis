namespace Aegis.Tests.Integration.Aml;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Infrastructure.Auth;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Identity;
using Microsoft.Extensions.DependencyInjection;

public sealed class RuleRbacTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _adminClient = null!;
    private HttpClient _analystClient = null!;
    private string _slug = null!;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _adminClient = _factory.CreateClient();
        _slug = $"rbac-{Guid.NewGuid():N}"[..16];

        var bootstrap = await _adminClient.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "RBAC Tenant",
            slug = _slug,
            adminEmail = $"admin@{_slug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        });
        bootstrap.EnsureSuccessStatusCode();
        var tenantId = new TenantId(
            (await bootstrap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var analyst = User.Create(
                tenantId,
                $"analyst@{_slug}.test",
                "Analyst User",
                hasher.Hash("Passw0rd!"),
                new[] { RoleNames.Analyst });
            await users.AddAsync(analyst);
            await uow.SaveChangesAsync();
        }

        _adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(_adminClient, $"admin@{_slug}.test"));

        _analystClient = _factory.CreateClient();
        _analystClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(_analystClient, $"analyst@{_slug}.test"));
    }

    public Task DisposeAsync()
    {
        _adminClient.Dispose();
        _analystClient.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Analyst_cannot_draft_or_activate_rules()
    {
        var rules = await _adminClient.GetAsync("/api/v1/rules");
        rules.EnsureSuccessStatusCode();
        var structuring = (await rules.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "STRUCTURING_001");
        var ruleId = structuring.GetProperty("ruleId").GetGuid();

        var definition = new
        {
            code = "STRUCTURING_001",
            name = "Structuring analyst blocked",
            focus = "CUSTOMER",
            schedule = new { frequency = "realtime", lookback = "24h" },
            conditions = new
            {
                all = new object[]
                {
                    new { field = "transaction_count_24h", @operator = ">=", value = 6 },
                    new { field = "transaction_sum_24h", @operator = ">=", value = 450000 },
                    new { field = "max_single_amount_24h", @operator = "<", value = 100000 }
                }
            },
            severity = "HIGH",
            riskScore = 85,
            actions = new[] { "CREATE_ALERT" }
        };

        var analystDraft = await _analystClient.PostAsJsonAsync(
            $"/api/v1/rules/{ruleId}/versions",
            new { definition });
        Assert.Equal(HttpStatusCode.Forbidden, analystDraft.StatusCode);

        var adminDraft = await _adminClient.PostAsJsonAsync(
            $"/api/v1/rules/{ruleId}/versions",
            new { definition });
        Assert.Equal(HttpStatusCode.Created, adminDraft.StatusCode);
        var versionId = (await adminDraft.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("ruleVersionId").GetGuid();

        var analystActivate = await _analystClient.PostAsync(
            $"/api/v1/rules/versions/{versionId}/activate",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, analystActivate.StatusCode);

        var adminActivate = await _adminClient.PostAsync(
            $"/api/v1/rules/versions/{versionId}/activate",
            null);
        Assert.Equal(HttpStatusCode.OK, adminActivate.StatusCode);
    }

    private async Task<string> LoginAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Passw0rd!",
            tenantSlug = _slug
        });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
    }
}
