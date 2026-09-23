namespace Aegis.Tests.Integration.Aml;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class HighRiskGeographyTests : IAsyncLifetime
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
        var slug = $"geo-{Guid.NewGuid():N}"[..16];
        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Geo Tenant",
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
            firstName = "Geo",
            lastName = "Test"
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
    public async Task High_risk_counterparty_country_triggers_geography_alert()
    {
        var ingest = await _client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = $"TX-GEO-{Guid.NewGuid():N}"[..28],
            accountId = _accountId,
            customerId = _customerId,
            amount = 15_000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            counterpartyCountry = "KP"
        });
        Assert.Equal(HttpStatusCode.Created, ingest.StatusCode);
        var body = await ingest.Content.ReadFromJsonAsync<JsonElement>();
        var evaluations = body.GetProperty("evaluations").EnumerateArray().ToList();
        var geo = evaluations.FirstOrDefault(e => e.GetProperty("ruleCode").GetString() == "HIGH_RISK_GEOGRAPHY_001");
        Assert.True(geo.ValueKind != JsonValueKind.Undefined, "Expected HIGH_RISK_GEOGRAPHY_001 evaluation");
        Assert.True(geo.GetProperty("isTriggered").GetBoolean());

        var alertIds = body.GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
        Assert.NotEmpty(alertIds);

        var alert = await _client.GetAsync($"/api/v1/alerts/{alertIds[0]}");
        alert.EnsureSuccessStatusCode();
        var alertBody = await alert.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("geography", alertBody.GetProperty("ruleName").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

        var rules = await _client.GetAsync("/api/v1/rules");
        rules.EnsureSuccessStatusCode();
        var codes = (await rules.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Select(r => r.GetProperty("code").GetString())
            .ToList();
        Assert.Contains("HIGH_RISK_GEOGRAPHY_001", codes);
    }

    [Fact]
    public async Task Domestic_counterparty_does_not_trigger_geography_alert()
    {
        var ingest = await _client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = $"TX-DOM-{Guid.NewGuid():N}"[..28],
            accountId = _accountId,
            customerId = _customerId,
            amount = 15_000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            counterpartyCountry = "KE"
        });
        Assert.Equal(HttpStatusCode.Created, ingest.StatusCode);
        var body = await ingest.Content.ReadFromJsonAsync<JsonElement>();
        var evaluations = body.GetProperty("evaluations").EnumerateArray().ToList();
        var geo = evaluations.FirstOrDefault(e => e.GetProperty("ruleCode").GetString() == "HIGH_RISK_GEOGRAPHY_001");
        Assert.True(geo.ValueKind != JsonValueKind.Undefined, "Expected HIGH_RISK_GEOGRAPHY_001 evaluation");
        Assert.False(geo.GetProperty("isTriggered").GetBoolean());
    }
}
