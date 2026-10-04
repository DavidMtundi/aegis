namespace Aegis.Tests.Integration.Screening;

using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Application.Risk;
using Aegis.Application.Screening;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;
using Aegis.Tests.Integration.Alerts;
using Microsoft.Extensions.DependencyInjection;

public sealed class ScreeningReviewConcurrencyTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public async Task InitializeAsync()
    {
        await _fx.InitializeAsync();
        (await _fx.AdminClient.PostAsync("/api/v1/screening/lists/refresh", null)).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task A_review_based_on_a_stale_read_is_a_conflict()
    {
        var created = await _fx.AdminClient.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL", country = "KE", firstName = "Zephaniah", lastName = "Mbwana"
        });
        var customerId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var matches = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/screening/matches?customerId={customerId}");
        var matchId = matches.GetProperty("items")[0].GetProperty("id").GetGuid();
        var tenant = new TenantId(_fx.TenantId);

        await using var first = _fx.Factory.Services.CreateAsyncScope();
        await using var second = _fx.Factory.Services.CreateAsyncScope();
        Assert.NotNull(await second.ServiceProvider.GetRequiredService<IScreeningMatchRepository>().GetAsync(tenant, matchId));

        await first.ServiceProvider.GetRequiredService<ICustomerScreeningService>()
            .ReviewAsync(tenant, matchId, ScreeningDecision.CONFIRM, "Same national ID", RiskActor.System);

        await Assert.ThrowsAsync<ScreeningMatchAlreadyReviewedException>(() =>
            second.ServiceProvider.GetRequiredService<ICustomerScreeningService>()
                .ReviewAsync(tenant, matchId, ScreeningDecision.FALSE_POSITIVE, "Different person", RiskActor.System));

        var detail = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/screening/matches/{matchId}");
        Assert.Equal("CONFIRMED", detail.GetProperty("match").GetProperty("status").GetString());
        Assert.Equal("Same national ID", detail.GetProperty("reviewReason").GetString());
    }
}
