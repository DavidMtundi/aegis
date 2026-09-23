namespace Aegis.Tests.Integration.Alerts;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class AlertDedupeTests : IAsyncLifetime
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

        var slug = $"alert-{Guid.NewGuid():N}"[..16];
        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Alert Tenant",
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
            firstName = "Sam",
            lastName = "Struct"
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
    public async Task Seven_95k_creates_one_alert_and_same_day_dedupes()
    {
        var baseTs = DateTimeOffset.UtcNow.AddHours(-2);
        Guid? firstAlertId = null;

        for (var i = 0; i < 7; i++)
        {
            var ingest = await _client.PostAsJsonAsync("/api/v1/transactions", new
            {
                externalReference = $"TX-ALERT-{i}-{Guid.NewGuid():N}"[..28],
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
            var body = await ingest.Content.ReadFromJsonAsync<JsonElement>();
            var alertIds = body.GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();

            if (i < 4)
            {
                Assert.Empty(alertIds);
            }
            else
            {
                // 5th+ should trigger (count>=5, sum>=450k, max<100k)
                Assert.Single(alertIds);
                firstAlertId ??= alertIds[0];
                Assert.Equal(firstAlertId, alertIds[0]);
            }
        }

        Assert.NotNull(firstAlertId);

        var alerts = await _client.GetAsync("/api/v1/alerts");
        Assert.Equal(HttpStatusCode.OK, alerts.StatusCode);
        var list = await alerts.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Single(list.EnumerateArray());

        var get = await _client.GetAsync($"/api/v1/alerts/{firstAlertId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var alert = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstAlertId, alert.GetProperty("id").GetGuid());
        Assert.NotEqual(Guid.Empty, alert.GetProperty("ruleVersionId").GetGuid());
        Assert.True(alert.GetProperty("transactionIds").GetArrayLength() >= 5);

        var audits = await _client.GetAsync("/api/v1/audit-events");
        Assert.Equal(HttpStatusCode.OK, audits.StatusCode);
        var auditList = await audits.Content.ReadFromJsonAsync<JsonElement>();
        var types = auditList.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Contains("TRANSACTION_INGESTED", types);
        Assert.Equal(1, types.Count(t => t == "ALERT_CREATED"));
    }
}
