namespace Aegis.Tests.Integration.Audit;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Alerts;

public sealed class AuditQueryTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    private Task<JsonElement> QueryAsync(string query)
        => _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/audit-events?{query}");

    [Fact]
    public async Task Filters_by_event_type_and_entity_and_includes_payload()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{alertId}/dismiss", new { reason = "Payroll" })).EnsureSuccessStatusCode();

        var byType = await QueryAsync("eventType=TRANSACTION_INGESTED");
        Assert.Equal(5, byType.GetProperty("totalCount").GetInt32());
        Assert.All(byType.GetProperty("items").EnumerateArray(),
            e => Assert.Equal("TRANSACTION_INGESTED", e.GetProperty("eventType").GetString()));

        var byEntity = await QueryAsync($"entityType=Alert&entityId={alertId}");
        var types = byEntity.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(new[] { "ALERT_DISMISSED", "ALERT_CREATED" }, types);
        var dismissed = byEntity.GetProperty("items")[0];
        Assert.Contains("Payroll", dismissed.GetProperty("afterState").GetString());
    }

    [Fact]
    public async Task Pages_newest_first_and_filters_by_time_range()
    {
        await _fx.CreateStructuringAlertAsync();

        var first = await QueryAsync("pageSize=2&page=1");
        var second = await QueryAsync("pageSize=2&page=2");
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(first.GetProperty("totalCount").GetInt32(), second.GetProperty("totalCount").GetInt32());
        var firstLast = first.GetProperty("items")[1].GetProperty("occurredAt").GetDateTimeOffset();
        var secondFirst = second.GetProperty("items")[0].GetProperty("occurredAt").GetDateTimeOffset();
        Assert.True(firstLast >= secondFirst);

        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var none = await QueryAsync($"from={future}");
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Rejects_inverted_time_range()
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));

        var response = await _fx.AdminClient.GetAsync($"/api/v1/audit-events?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
