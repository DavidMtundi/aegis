namespace Aegis.Tests.Integration.Aml;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Alerts;

public sealed class RuleLookbackTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    private async Task ActivateSevenDayStructuringAsync()
    {
        var rules = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/rules");
        var ruleId = rules.EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "STRUCTURING_001")
            .GetProperty("ruleId").GetGuid();

        var draft = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/rules/{ruleId}/versions", new
        {
            definition = new
            {
                code = "STRUCTURING_001",
                name = "Structuring 7d",
                focus = "CUSTOMER",
                schedule = new { frequency = "realtime", lookback = "7d" },
                conditions = new
                {
                    all = new object[]
                    {
                        new { field = "transaction_count", @operator = ">=", value = 3 },
                        new { field = "transaction_sum", @operator = ">=", value = 150000 }
                    }
                },
                severity = "HIGH",
                riskScore = 80,
                actions = new[] { "CREATE_ALERT" }
            }
        });
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var versionId = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ruleVersionId").GetGuid();
        (await _fx.AdminClient.PostAsync($"/api/v1/rules/versions/{versionId}/activate", null)).EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<Guid>> IngestAsync(Guid accountId, string currency, DateTimeOffset timestamp)
    {
        var response = await _fx.AdminClient.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = $"LB-{Guid.NewGuid():N}"[..20],
            accountId,
            customerId = _fx.CustomerId,
            amount = 60_000m,
            currency,
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
    }

    [Fact]
    public async Task Seven_day_lookback_sees_transactions_older_than_24h()
    {
        await ActivateSevenDayStructuringAsync();
        var now = DateTimeOffset.UtcNow;

        Assert.Empty(await IngestAsync(_fx.AccountId, "KES", now.AddDays(-3)));
        Assert.Empty(await IngestAsync(_fx.AccountId, "KES", now.AddDays(-2)));
        Assert.Single(await IngestAsync(_fx.AccountId, "KES", now.AddHours(-1)));
    }

    [Fact]
    public async Task Sums_do_not_mix_currencies()
    {
        await ActivateSevenDayStructuringAsync();
        var account = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/customers/{_fx.CustomerId}/accounts",
            new { accountType = "MOBILE_WALLET", currency = "USD" });
        account.EnsureSuccessStatusCode();
        var usdAccountId = (await account.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;

        await IngestAsync(_fx.AccountId, "KES", now.AddDays(-2));
        await IngestAsync(usdAccountId, "USD", now.AddDays(-1));

        Assert.Empty(await IngestAsync(_fx.AccountId, "KES", now.AddHours(-1)));
    }

    [Fact]
    public async Task Transaction_currency_must_match_account_currency()
    {
        var response = await _fx.AdminClient.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = "LB-MISMATCH",
            accountId = _fx.AccountId,
            customerId = _fx.CustomerId,
            amount = 100m,
            currency = "USD",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
