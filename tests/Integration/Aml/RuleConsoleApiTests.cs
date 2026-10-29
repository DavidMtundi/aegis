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

public sealed class RuleConsoleApiTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _reviewer = null!;
    private string _slug = null!;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _admin = _factory.CreateClient();
        _slug = $"rcon-{Guid.NewGuid():N}"[..16];

        var bootstrap = await _admin.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Rule Console Tenant",
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
            await users.AddAsync(User.Create(
                tenantId,
                $"reviewer@{_slug}.test",
                "Reviewer",
                hasher.Hash("Passw0rd!"),
                new[] { RoleNames.Reviewer }));
            await uow.SaveChangesAsync();
        }

        _admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(_admin, $"admin@{_slug}.test"));
        _reviewer = _factory.CreateClient();
        _reviewer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(_reviewer, $"reviewer@{_slug}.test"));
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        _reviewer.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Validate_update_draft_and_pending_queue()
    {
        var code = $"ED_{Guid.NewGuid():N}"[..14];
        var definition = new
        {
            code,
            name = "Editable",
            focus = "CUSTOMER",
            schedule = new { frequency = "realtime", lookback = "24h" },
            conditions = new { all = new object[] { new { field = "transaction_count_24h", @operator = ">=", value = 3 } } },
            severity = "MEDIUM",
            riskScore = 40,
            actions = new[] { "CREATE_ALERT" }
        };

        var invalid = await _admin.PostAsJsonAsync("/api/v1/rules/validate", new
        {
            definition = new
            {
                code = "",
                name = "x",
                schedule = new { frequency = "realtime", lookback = "24h" },
                conditions = new { all = new object[] { new { field = "transaction_count_24h", @operator = ">=", value = 3 } } },
                severity = "MEDIUM",
                riskScore = 40,
                actions = new[] { "CREATE_ALERT" }
            }
        });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invalidBody.GetProperty("valid").GetBoolean());
        Assert.True(invalidBody.GetProperty("errors").GetArrayLength() > 0);

        var create = await _admin.PostAsJsonAsync("/api/v1/rules", new
        {
            code,
            name = "Editable",
            description = "",
            scenarioType = "CUSTOM",
            definition
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var versionId = created.GetProperty("ruleVersionId").GetGuid();

        var updatedDef = new
        {
            code,
            name = "Editable v1b",
            focus = "CUSTOMER",
            schedule = new { frequency = "realtime", lookback = "24h" },
            conditions = new { all = new object[] { new { field = "transaction_count_24h", @operator = ">=", value = 4 } } },
            severity = "HIGH",
            riskScore = 70,
            actions = new[] { "CREATE_ALERT" }
        };
        var update = await _admin.PutAsJsonAsync($"/api/v1/rules/versions/{versionId}", new { definition = updatedDef });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Editable v1b", (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());

        (await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/submit", null)).EnsureSuccessStatusCode();

        var queue = await _reviewer.GetAsync("/api/v1/rules/pending-approval");
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        var pending = await queue.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(pending.EnumerateArray(), r => r.GetProperty("ruleVersionId").GetGuid() == versionId);
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
