namespace Aegis.Tests.Integration.Screening;

using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Screening;
using Aegis.Shared.Domain;
using Aegis.Tests.Integration.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class ScreeningRiskTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public async Task InitializeAsync()
    {
        await _fx.InitializeAsync();
        (await _fx.AdminClient.PostAsync("/api/v1/screening/lists/refresh", null)).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Confirmed_sanctions_match_makes_the_customer_critical()
    {
        var created = await _fx.AdminClient.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL", country = "KE", firstName = "Zephaniah", lastName = "Mbwana"
        });
        var customerId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var matches = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/screening/matches?customerId={customerId}");
        var matchId = matches.GetProperty("items")[0].GetProperty("id").GetGuid();

        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/confirm", new { reason = "Same ID" }))
            .EnsureSuccessStatusCode();

        var current = (await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customerId}/risk")).GetProperty("current");
        Assert.Equal("CRITICAL", current.GetProperty("band").GetString());
        Assert.Equal("SCREENING_CONFIRMED", current.GetProperty("trigger").GetString());
        var sanctions = current.GetProperty("contributions").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == "SANCTIONS_MATCH");
        Assert.Equal(100, sanctions.GetProperty("points").GetInt32());
    }

    [Fact]
    public async Task Sync_adds_screening_factors_to_a_model_that_predates_them_once()
    {
        var tenant = await StripScreeningFactorsFromAllVersionsAsync();

        Assert.Equal(1, await SyncAsync(tenant));
        Assert.Contains("SANCTIONS_MATCH", await ActiveFactorTypesAsync());
        Assert.Equal(0, await SyncAsync(tenant));
    }

    [Fact]
    public async Task Sync_keeps_an_admins_removal_of_screening_factors()
    {
        var model = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/risk/model");
        var withoutScreening = model.GetProperty("factors").EnumerateArray()
            .Where(f => !f.GetProperty("type").GetString()!.EndsWith("_MATCH"))
            .Select(f => JsonSerializer.Deserialize<object>(f.GetRawText()))
            .ToList();
        (await _fx.AdminClient.PutAsJsonAsync("/api/v1/risk/model", new { factors = withoutScreening, bands = model.GetProperty("bands") }))
            .EnsureSuccessStatusCode();

        Assert.Equal(0, await SyncAsync(await TenantIdAsync()));
        Assert.DoesNotContain("SANCTIONS_MATCH", await ActiveFactorTypesAsync());
    }

    private async Task<TenantId> TenantIdAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        var id = await db.Tenants.IgnoreQueryFilters().Where(t => t.Slug == _fx.Slug).Select(t => t.Id).SingleAsync();
        return new TenantId(id);
    }

    private async Task<TenantId> StripScreeningFactorsFromAllVersionsAsync()
    {
        await _fx.AdminClient.GetAsync("/api/v1/risk/model");
        var tenant = await TenantIdAsync();
        using var scope = _fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE risk.risk_models
            SET factors_json = (
                SELECT jsonb_agg(f) FROM jsonb_array_elements(factors_json) f
                WHERE f->>'Type' NOT IN ('SANCTIONS_MATCH', 'PEP_MATCH', 'WATCHLIST_MATCH'))
            WHERE tenant_id = {tenant.Value}
            """);
        return tenant;
    }

    private async Task<int> SyncAsync(TenantId tenant)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ScreeningRiskFactorSync>().SyncAsync(tenant);
    }

    private async Task<List<string?>> ActiveFactorTypesAsync()
    {
        var model = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/risk/model");
        return model.GetProperty("factors").EnumerateArray().Select(f => f.GetProperty("type").GetString()).ToList();
    }
}
