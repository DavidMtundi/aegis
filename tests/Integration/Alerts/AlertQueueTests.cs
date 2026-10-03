namespace Aegis.Tests.Integration.Alerts;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class AlertQueueTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task List_and_detail_carry_customer_and_assignee_names()
    {
        var alertId = await _fx.SeedAlertAsync();
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{alertId}/assign", new { })).EnsureSuccessStatusCode();

        var list = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/alerts");
        var detail = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId}");

        foreach (var alert in new[] { list.GetProperty("items")[0], detail })
        {
            Assert.Equal("Sam Struct", alert.GetProperty("customerName").GetString());
            Assert.Equal("KE", alert.GetProperty("customerCountry").GetString());
            Assert.Equal("Admin", alert.GetProperty("assigneeName").GetString());
        }
    }

    [Fact]
    public async Task Views_filter_open_mine_unassigned_and_past_sla()
    {
        var mine = await _fx.SeedAlertAsync();
        var unassigned = await _fx.SeedAlertAsync();
        var old = await _fx.SeedAlertAsync();
        var dismissed = await _fx.SeedAlertAsync();
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{mine}/assign", new { })).EnsureSuccessStatusCode();
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{dismissed}/dismiss", new { reason = "noise" })).EnsureSuccessStatusCode();
        await BackdateAsync(old, TimeSpan.FromDays(10));

        Assert.Equal(new[] { mine, unassigned, old }.OrderBy(x => x), await IdsAsync("open"));
        Assert.Equal(new[] { mine }, await IdsAsync("mine"));
        Assert.Equal(new[] { unassigned, old }.OrderBy(x => x), await IdsAsync("unassigned"));
        Assert.Equal(new[] { old }, await IdsAsync("pastSla"));
        Assert.Equal(4, (await IdsAsync(null)).Count);

        var bad = await _fx.AdminClient.GetAsync("/api/v1/alerts?view=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task List_filters_by_customer()
    {
        await _fx.SeedAlertAsync();

        var match = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts?customerId={_fx.CustomerId}");
        var none = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts?customerId={Guid.NewGuid()}");

        Assert.Equal(1, match.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Sla_settings_are_readable_by_any_signed_in_user()
    {
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });

        var sla = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/settings/sla");

        Assert.Equal(3, sla.GetProperty("alertSlaDays").GetInt32());
        Assert.Equal(14, sla.GetProperty("caseSlaDays").GetInt32());
    }

    private async Task<IReadOnlyList<Guid>> IdsAsync(string? view)
    {
        var url = view is null ? "/api/v1/alerts" : $"/api/v1/alerts?view={view}";
        var body = await _fx.AdminClient.GetFromJsonAsync<JsonElement>(url);
        return body.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).OrderBy(x => x).ToList();
    }

    private async Task BackdateAsync(Guid alertId, TimeSpan age)
    {
        await using var scope = _fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        var triggeredAt = DateTimeOffset.UtcNow - age;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE alerts.alerts SET \"TriggeredAt\" = {triggeredAt} WHERE \"Id\" = {alertId}");
    }
}
