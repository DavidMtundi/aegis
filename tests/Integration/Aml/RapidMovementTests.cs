namespace Aegis.Tests.Integration.Aml;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class RapidMovementTests : IAsyncLifetime
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
        var slug = $"rapid-{Guid.NewGuid():N}"[..16];
        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Rapid",
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
            firstName = "Rapid",
            lastName = "Move"
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
    public async Task Credit_then_debit_within_hour_triggers_rapid_movement()
    {
        var ts = DateTimeOffset.UtcNow.AddMinutes(-30);
        var credit = await _client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = $"TX-IN-{Guid.NewGuid():N}"[..28],
            accountId = _accountId,
            customerId = _customerId,
            amount = 100_000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = ts
        });
        Assert.Equal(HttpStatusCode.Created, credit.StatusCode);

        var debit = await _client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = $"TX-OUT-{Guid.NewGuid():N}"[..28],
            accountId = _accountId,
            customerId = _customerId,
            amount = 95_000m,
            currency = "KES",
            direction = "DEBIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = ts.AddMinutes(10)
        });
        Assert.Equal(HttpStatusCode.Created, debit.StatusCode);
        var body = await debit.Content.ReadFromJsonAsync<JsonElement>();
        var alertIds = body.GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
        Assert.NotEmpty(alertIds);

        var alert = await _client.GetAsync($"/api/v1/alerts/{alertIds[0]}");
        alert.EnsureSuccessStatusCode();
        var alertBody = await alert.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Rapid", alertBody.GetProperty("ruleName").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

        var rules = await _client.GetAsync("/api/v1/rules");
        rules.EnsureSuccessStatusCode();
        var codes = (await rules.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Select(r => r.GetProperty("code").GetString())
            .ToList();
        Assert.Contains("STRUCTURING_001", codes);
        Assert.Contains("RAPID_MOVEMENT_001", codes);
    }
}
