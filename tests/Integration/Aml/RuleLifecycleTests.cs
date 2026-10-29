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

public sealed class RuleLifecycleTests : IAsyncLifetime
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
        _slug = $"rule-{Guid.NewGuid():N}"[..16];

        var bootstrap = await _admin.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Rule Tenant",
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
            var reviewer = User.Create(
                tenantId,
                $"reviewer@{_slug}.test",
                "Reviewer User",
                hasher.Hash("Passw0rd!"),
                new[] { RoleNames.Reviewer });
            await users.AddAsync(reviewer);
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
    public async Task Maker_checker_required_before_activating_new_version()
    {
        var rules = await _admin.GetAsync("/api/v1/rules");
        rules.EnsureSuccessStatusCode();
        var structuring = (await rules.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "STRUCTURING_001");
        var ruleId = structuring.GetProperty("ruleId").GetGuid();

        var draft = await _admin.PostAsJsonAsync($"/api/v1/rules/{ruleId}/versions", new
        {
            definition = SampleDefinition("STRUCTURING_001", "Structuring v2", riskScore: 85)
        });
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var versionId = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ruleVersionId").GetGuid();

        var activateTooSoon = await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, activateTooSoon.StatusCode);

        (await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/submit", null)).EnsureSuccessStatusCode();

        var selfApprove = await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, selfApprove.StatusCode);

        var approve = await _reviewer.PostAsync($"/api/v1/rules/versions/{versionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("APPROVED", (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var activate = await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var active = await activate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ACTIVE", active.GetProperty("status").GetString());
        Assert.Equal(2, active.GetProperty("versionNumber").GetInt32());
    }

    [Fact]
    public async Task Admin_can_create_then_go_live_via_maker_checker_then_suspend_resume_retire()
    {
        var code = $"CUSTOM_{Guid.NewGuid():N}"[..16];
        var definition = SampleDefinition(code, "Custom lifecycle rule");

        var create = await _admin.PostAsJsonAsync("/api/v1/rules", new
        {
            code,
            name = "Custom lifecycle rule",
            description = "Phase 4.2",
            scenarioType = "CUSTOM",
            definition
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DRAFT", created.GetProperty("status").GetString());
        var ruleId = created.GetProperty("ruleId").GetGuid();
        var versionId = created.GetProperty("ruleVersionId").GetGuid();

        var duplicate = await _admin.PostAsJsonAsync("/api/v1/rules", new
        {
            code,
            name = "Dup",
            description = "",
            scenarioType = "CUSTOM",
            definition
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await PromoteToActiveAsync(versionId);

        var suspend = await _admin.PostAsync($"/api/v1/rules/{ruleId}/suspend", null);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Equal("SUSPENDED", (await suspend.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var resume = await _admin.PostAsync($"/api/v1/rules/{ruleId}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);

        var retire = await _admin.PostAsync($"/api/v1/rules/{ruleId}/retire", null);
        Assert.Equal(HttpStatusCode.OK, retire.StatusCode);
        Assert.Equal("RETIRED", (await retire.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var draft = await _admin.PostAsJsonAsync($"/api/v1/rules/{ruleId}/versions", new { definition });
        Assert.Equal(HttpStatusCode.Conflict, draft.StatusCode);
    }

    [Fact]
    public async Task Creator_can_withdraw_pending_approval()
    {
        var code = $"WD_{Guid.NewGuid():N}"[..14];
        var create = await _admin.PostAsJsonAsync("/api/v1/rules", new
        {
            code,
            name = "Withdraw me",
            description = "",
            scenarioType = "CUSTOM",
            definition = SampleDefinition(code, "Withdraw me")
        });
        create.EnsureSuccessStatusCode();
        var versionId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ruleVersionId").GetGuid();

        (await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/submit", null)).EnsureSuccessStatusCode();
        var withdraw = await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);
        Assert.Equal("DRAFT", (await withdraw.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    private async Task PromoteToActiveAsync(Guid versionId)
    {
        (await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/submit", null)).EnsureSuccessStatusCode();
        (await _reviewer.PostAsync($"/api/v1/rules/versions/{versionId}/approve", null)).EnsureSuccessStatusCode();
        var activate = await _admin.PostAsync($"/api/v1/rules/versions/{versionId}/activate", null);
        activate.EnsureSuccessStatusCode();
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

    private static object SampleDefinition(string code, string name, int riskScore = 40) => new
    {
        code,
        name,
        focus = "CUSTOMER",
        schedule = new { frequency = "realtime", lookback = "24h" },
        conditions = new
        {
            all = new object[]
            {
                new { field = "transaction_count_24h", @operator = ">=", value = 3 }
            }
        },
        severity = "MEDIUM",
        riskScore,
        actions = new[] { "CREATE_ALERT" }
    };
}
