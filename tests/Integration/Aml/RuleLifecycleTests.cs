namespace Aegis.Tests.Integration.Aml;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class RuleLifecycleTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _client = _factory.CreateClient();
        var slug = $"rule-{Guid.NewGuid():N}"[..16];
        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Rule Tenant",
            slug,
            adminEmail = $"admin@{slug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        })).EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"admin@{slug}.test",
            password = "Passw0rd!",
            tenantSlug = slug
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Admin_can_create_draft_and_activate_new_version()
    {
        var rules = await _client.GetAsync("/api/v1/rules");
        rules.EnsureSuccessStatusCode();
        var structuring = (await rules.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "STRUCTURING_001");
        var ruleId = structuring.GetProperty("ruleId").GetGuid();

        var draft = await _client.PostAsJsonAsync($"/api/v1/rules/{ruleId}/versions", new
        {
            definition = new
            {
                code = "STRUCTURING_001",
                name = "Structuring v2",
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
            }
        });
        if (draft.StatusCode != HttpStatusCode.Created)
        {
            Assert.Fail($"Draft failed: {(int)draft.StatusCode} {await draft.Content.ReadAsStringAsync()}");
        }
        var draftBody = await draft.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DRAFT", draftBody.GetProperty("status").GetString());
        var versionId = draftBody.GetProperty("ruleVersionId").GetGuid();

        var activate = await _client.PostAsync($"/api/v1/rules/versions/{versionId}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var active = await activate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ACTIVE", active.GetProperty("status").GetString());
        Assert.Equal(2, active.GetProperty("versionNumber").GetInt32());

        var versions = await _client.GetAsync($"/api/v1/rules/{ruleId}/versions");
        versions.EnsureSuccessStatusCode();
        var list = await versions.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetArrayLength() >= 2);
    }
}
