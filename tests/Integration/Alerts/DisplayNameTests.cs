namespace Aegis.Tests.Integration.Alerts;

using System.Net.Http.Json;
using System.Text.Json;

public sealed class DisplayNameTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Cases_carry_customer_assignee_and_linked_alert_summaries()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var created = await _fx.AdminClient.PostAsync($"/api/v1/alerts/{alertId}/create-case", null);
        created.EnsureSuccessStatusCode();
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/assign", new { })).EnsureSuccessStatusCode();

        var detail = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/cases/{caseId}");
        var list = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/cases");

        foreach (var c in new[] { detail, list.GetProperty("items")[0] })
        {
            Assert.Equal("Sam Struct", c.GetProperty("customerName").GetString());
            Assert.Equal("Admin", c.GetProperty("assigneeName").GetString());
            var linked = c.GetProperty("linkedAlerts");
            Assert.Equal(1, linked.GetArrayLength());
            Assert.Equal(alertId, linked[0].GetProperty("id").GetGuid());
            Assert.False(string.IsNullOrEmpty(linked[0].GetProperty("ruleName").GetString()));
            Assert.False(string.IsNullOrEmpty(linked[0].GetProperty("severity").GetString()));
            Assert.False(string.IsNullOrEmpty(linked[0].GetProperty("status").GetString()));
        }
    }

    [Fact]
    public async Task Transactions_carry_customer_name()
    {
        await _fx.IngestStructuringCreditAsync(DateTimeOffset.UtcNow.AddMinutes(-5));

        var list = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/transactions");
        var item = list.GetProperty("items")[0];
        Assert.Equal("Sam Struct", item.GetProperty("customerName").GetString());

        var detail = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/transactions/{item.GetProperty("id").GetGuid()}");
        Assert.Equal("Sam Struct", detail.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task Customer_list_carries_latest_risk_band()
    {
        var list = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/customers");
        var item = list.GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == _fx.CustomerId);
        var risk = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{_fx.CustomerId}/risk");

        Assert.Equal(risk.GetProperty("current").GetProperty("band").GetString(), item.GetProperty("riskBand").GetString());
    }
}
