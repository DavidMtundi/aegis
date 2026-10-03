namespace Aegis.Tests.Integration.Risk;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;

public sealed class CustomerRiskApiTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task New_customer_is_scored_with_the_default_model()
    {
        var risk = await GetRiskAsync(_fx.CustomerId);

        var current = risk.GetProperty("current");
        Assert.Equal("LOW", current.GetProperty("band").GetString());
        Assert.Equal(1, current.GetProperty("modelVersion").GetInt32());
        Assert.Equal("CUSTOMER_CREATED", current.GetProperty("trigger").GetString());
        Assert.Equal(6, current.GetProperty("contributions").GetArrayLength());
        Assert.Equal(1, risk.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task High_risk_country_lands_in_medium_band()
    {
        var created = await _fx.AdminClient.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL", country = "KP", firstName = "Kim", lastName = "Geo"
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var current = (await GetRiskAsync(id)).GetProperty("current");

        Assert.Equal(30, current.GetProperty("score").GetInt32());
        Assert.Equal("MEDIUM", current.GetProperty("band").GetString());
        Assert.Equal(30, Contribution(current, "GEOGRAPHY").GetProperty("points").GetInt32());
    }

    [Fact]
    public async Task New_alert_rescores_the_customer()
    {
        var before = (await GetRiskAsync(_fx.CustomerId)).GetProperty("current").GetProperty("score").GetInt32();

        await _fx.CreateStructuringAlertAsync();

        var risk = await GetRiskAsync(_fx.CustomerId);
        var current = risk.GetProperty("current");
        Assert.Equal("ALERT_CREATED", current.GetProperty("trigger").GetString());
        Assert.True(current.GetProperty("score").GetInt32() > before);
        Assert.Equal(5, Contribution(current, "OPEN_ALERTS").GetProperty("points").GetInt32());
        Assert.Equal(2, risk.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Recalculate_requires_customer_write_and_appends_history()
    {
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });
        var forbidden = await viewer.PostAsync($"/api/v1/customers/{_fx.CustomerId}/risk/recalculate", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var response = await _fx.AdminClient.PostAsync($"/api/v1/customers/{_fx.CustomerId}/risk/recalculate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MANUAL", body.GetProperty("trigger").GetString());
        Assert.Equal(2, (await GetRiskAsync(_fx.CustomerId)).GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_customer_is_404()
    {
        var get = await _fx.AdminClient.GetAsync($"/api/v1/customers/{Guid.NewGuid()}/risk");
        var post = await _fx.AdminClient.PostAsync($"/api/v1/customers/{Guid.NewGuid()}/risk/recalculate", null);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    [Fact]
    public async Task Updating_the_model_creates_an_audited_new_version()
    {
        var updated = await _fx.AdminClient.PutAsJsonAsync("/api/v1/risk/model", new
        {
            factors = new object[]
            {
                new { type = "GEOGRAPHY", weight = 60, countries = new[] { "ke" } },
                new { type = "OPEN_ALERTS", weight = 40, pointsEach = 10 }
            },
            bands = new { medium = 20, high = 50, critical = 80 }
        });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var model = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, model.GetProperty("version").GetInt32());
        Assert.Equal("KE", model.GetProperty("factors")[0].GetProperty("countries")[0].GetString());
        Assert.Equal(2, model.GetProperty("versions").GetArrayLength());

        var rescored = await _fx.AdminClient.PostAsync($"/api/v1/customers/{_fx.CustomerId}/risk/recalculate", null);
        var score = await rescored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, score.GetProperty("modelVersion").GetInt32());
        Assert.Equal(60, score.GetProperty("score").GetInt32());
        Assert.Equal("HIGH", score.GetProperty("band").GetString());

        var audit = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/audit-events?eventType=RISK_MODEL_UPDATED");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "RISK_MODEL_UPDATED");
    }

    [Fact]
    public async Task Invalid_model_is_rejected_and_only_admins_can_update()
    {
        var invalid = await _fx.AdminClient.PutAsJsonAsync("/api/v1/risk/model", new
        {
            factors = new object[] { new { type = "GEOGRAPHY", weight = 30 } },
            bands = new { medium = 50, high = 40, critical = 80 }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var analyst = await _fx.CreateUserClientAsync($"analyst@{_fx.Slug}.test", new[] { RoleNames.Analyst });
        var forbidden = await analyst.PutAsJsonAsync("/api/v1/risk/model", new
        {
            factors = new object[] { new { type = "OPEN_ALERTS", weight = 40, pointsEach = 10 } },
            bands = new { medium = 20, high = 50, critical = 80 }
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var model = await analyst.GetFromJsonAsync<JsonElement>("/api/v1/risk/model");
        Assert.Equal(1, model.GetProperty("version").GetInt32());
    }

    private async Task<JsonElement> GetRiskAsync(Guid customerId)
    {
        var response = await _fx.AdminClient.GetAsync($"/api/v1/customers/{customerId}/risk");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Contribution(JsonElement score, string type)
        => score.GetProperty("contributions").EnumerateArray().Single(c => c.GetProperty("type").GetString() == type);
}
