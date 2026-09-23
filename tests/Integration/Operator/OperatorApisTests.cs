namespace Aegis.Tests.Integration.Operator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class OperatorApisTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _customerId;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _client = _factory.CreateClient();

        var slug = $"ops-{Guid.NewGuid():N}"[..16];
        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Ops Tenant",
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

        var customerResp = await _client.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL",
            country = "KE",
            firstName = "Ada",
            lastName = "Analyst"
        });
        customerResp.EnsureSuccessStatusCode();
        _customerId = (await customerResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var accountResp = await _client.PostAsJsonAsync($"/api/v1/customers/{_customerId}/accounts", new
        {
            accountType = "MOBILE_WALLET",
            currency = "KES"
        });
        accountResp.EnsureSuccessStatusCode();
        _accountId = (await accountResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Customers_transactions_rules_and_alert_actions_work()
    {
        var customers = await _client.GetAsync("/api/v1/customers?q=Ada&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, customers.StatusCode);
        var customerList = await customers.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, customerList.GetProperty("totalCount").GetInt32());
        Assert.Equal(_customerId, customerList.GetProperty("items")[0].GetProperty("id").GetGuid());

        var baseTs = DateTimeOffset.UtcNow.AddHours(-1);
        Guid? alertId = null;
        for (var i = 0; i < 5; i++)
        {
            var ingest = await _client.PostAsJsonAsync("/api/v1/transactions", new
            {
                externalReference = $"TX-OPS-{i}-{Guid.NewGuid():N}"[..28],
                accountId = _accountId,
                customerId = _customerId,
                amount = 95_000m,
                currency = "KES",
                direction = "CREDIT",
                transactionType = "TRANSFER",
                channel = "MOBILE",
                timestamp = baseTs.AddMinutes(i)
            });
            Assert.Equal(HttpStatusCode.Created, ingest.StatusCode);
            var ingestBody = await ingest.Content.ReadFromJsonAsync<JsonElement>();
            var alertIds = ingestBody.GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
            if (alertIds.Count > 0)
                alertId = alertIds[0];
        }

        Assert.NotNull(alertId);

        var txs = await _client.GetAsync($"/api/v1/transactions?customerId={_customerId}&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, txs.StatusCode);
        var txList = await txs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(txList.GetProperty("totalCount").GetInt32() >= 5);

        var rules = await _client.GetAsync("/api/v1/rules");
        Assert.Equal(HttpStatusCode.OK, rules.StatusCode);
        var ruleList = await rules.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ruleList.GetArrayLength() >= 1);
        var ruleId = ruleList[0].GetProperty("ruleId").GetGuid();
        var ruleDetail = await _client.GetAsync($"/api/v1/rules/{ruleId}");
        Assert.Equal(HttpStatusCode.OK, ruleDetail.StatusCode);
        var ruleBody = await ruleDetail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("STRUCTURING_001", ruleBody.GetProperty("code").GetString());
        Assert.True(ruleBody.TryGetProperty("definition", out _));

        var filtered = await _client.GetAsync("/api/v1/alerts?status=OPEN&severity=HIGH&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var alertList = await filtered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(alertList.GetProperty("totalCount").GetInt32() >= 1);

        var assign = await _client.PostAsJsonAsync($"/api/v1/alerts/{alertId}/assign", new { assignedTo = "analyst@example.com" });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var assigned = await assign.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ASSIGNED", assigned.GetProperty("status").GetString());
        Assert.Equal("analyst@example.com", assigned.GetProperty("assignedTo").GetString());

        var dismiss = await _client.PostAsync($"/api/v1/alerts/{alertId}/dismiss", null);
        Assert.Equal(HttpStatusCode.OK, dismiss.StatusCode);
        var dismissed = await dismiss.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DISMISSED", dismissed.GetProperty("status").GetString());

        var audits = await _client.GetAsync("/api/v1/audit-events");
        audits.EnsureSuccessStatusCode();
        var auditTypes = (await audits.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Select(e => e.GetProperty("eventType").GetString())
            .ToList();
        Assert.Contains("ALERT_ASSIGNED", auditTypes);
        Assert.Contains("ALERT_DISMISSED", auditTypes);
    }
}
