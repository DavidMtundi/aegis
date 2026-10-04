namespace Aegis.Tests.Integration.Screening;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;

public sealed class ScreeningApiTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public async Task InitializeAsync()
    {
        await _fx.InitializeAsync();
        (await _fx.AdminClient.PostAsync("/api/v1/screening/lists/refresh", null)).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Lists_report_both_global_sources()
    {
        var body = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/screening/lists");
        var sources = body.GetProperty("sources").EnumerateArray().ToDictionary(
            s => s.GetProperty("source").GetString()!, s => s.GetProperty("entryCount").GetInt32());
        Assert.Equal(4, sources["UN"]);
        Assert.Equal(2, sources["OFAC"]);
        Assert.Equal(0, body.GetProperty("tenantList").GetProperty("entryCount").GetInt32());
    }

    [Fact]
    public async Task Creating_a_listed_customer_records_a_pending_match()
    {
        var customerId = await CreateIndividualAsync("Zephaniah", "Mbwana", "1978-04-28");

        var match = await SingleMatchAsync(customerId);

        Assert.Equal("PENDING_REVIEW", match.GetProperty("status").GetString());
        Assert.Equal("UN", match.GetProperty("source").GetString());
        Assert.Equal("SANCTIONS", match.GetProperty("category").GetString());
        Assert.True(match.GetProperty("birthYearMatched").GetBoolean());
        Assert.True(match.GetProperty("score").GetDouble() >= 0.88);
        Assert.Equal("CUSTOMER_CREATED", match.GetProperty("trigger").GetString());
    }

    [Fact]
    public async Task Birth_year_outside_the_listed_range_is_not_a_match()
    {
        var customerId = await CreateIndividualAsync("Zephaniah", "Mbwana", "1990-01-01");
        Assert.Equal(0, (await MatchesAsync(customerId)).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Future_date_of_birth_is_rejected()
    {
        var response = await _fx.AdminClient.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL", country = "KE", firstName = "Future", lastName = "Person",
            dateOfBirth = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd")
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirming_raises_a_critical_alert_once()
    {
        var customerId = await CreateIndividualAsync("Zephaniah", "Mbwana", null);
        var matchId = (await SingleMatchAsync(customerId)).GetProperty("id").GetGuid();

        var confirm = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/confirm", new { reason = "Same national ID" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var detail = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/screening/matches/{matchId}");
        Assert.Equal("CONFIRMED", detail.GetProperty("match").GetProperty("status").GetString());
        Assert.Equal("Same national ID", detail.GetProperty("reviewReason").GetString());
        var alertId = detail.GetProperty("alertId").GetGuid();

        var alert = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId}");
        Assert.Equal("CRITICAL", alert.GetProperty("severity").GetString());
        Assert.Equal("Confirmed sanctions match", alert.GetProperty("ruleName").GetString());

        var again = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/confirm", new { reason = "Twice" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Dismissing_needs_a_reason_and_a_rescreen_does_not_recreate_the_match()
    {
        var customerId = await CreateIndividualAsync("Zephaniah", "Mbwana", null);
        var matchId = (await SingleMatchAsync(customerId)).GetProperty("id").GetGuid();

        var blank = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/dismiss", new { reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var dismiss = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/dismiss", new { reason = "Different person: born 1990" });
        Assert.Equal(HttpStatusCode.OK, dismiss.StatusCode);

        var rescreen = await _fx.AdminClient.PostAsync($"/api/v1/customers/{customerId}/screen", null);
        Assert.Equal(0, (await rescreen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("newMatches").GetInt32());
        Assert.Equal("FALSE_POSITIVE", (await SingleMatchAsync(customerId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Viewers_read_reviewers_decide_admins_manage_lists()
    {
        var customerId = await CreateIndividualAsync("Zephaniah", "Mbwana", null);
        var matchId = (await SingleMatchAsync(customerId)).GetProperty("id").GetGuid();
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });
        var analyst = await _fx.CreateUserClientAsync($"analyst@{_fx.Slug}.test", new[] { RoleNames.Analyst });
        var reviewer = await _fx.CreateUserClientAsync($"reviewer@{_fx.Slug}.test", new[] { RoleNames.Reviewer });

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/screening/matches")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/confirm", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/confirm", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsync("/api/v1/screening/lists/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/dismiss", new { reason = "Different person" })).StatusCode);
    }

    [Fact]
    public async Task Uploaded_list_screens_existing_customers()
    {
        var customerId = await CreateIndividualAsync("Grace", "Akinyi Otieno", null);

        var upload = await UploadAsync(
            "reference,name,category,kind\nPEP-1,Grace Akinyi Otieno,PEP,INDIVIDUAL\n");

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var body = await upload.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("entryCount").GetInt32());
        Assert.Equal(1, body.GetProperty("newMatches").GetInt32());
        var match = await SingleMatchAsync(customerId);
        Assert.Equal("TENANT", match.GetProperty("source").GetString());
        Assert.Equal("PEP", match.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Invalid_list_is_rejected_with_line_errors()
    {
        var upload = await UploadAsync("reference,name,category,kind\nX-1,Someone,FRIEND,INDIVIDUAL\n");
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        var errors = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.StartsWith("Line 2:", errors[0].GetString());
    }

    [Fact]
    public async Task Another_tenant_cannot_see_or_review_the_match()
    {
        var customerId = await CreateIndividualAsync("Zephaniah", "Mbwana", null);
        var matchId = (await SingleMatchAsync(customerId)).GetProperty("id").GetGuid();
        var other = new AlertTestFixture();
        await other.InitializeAsync();
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await other.AdminClient.GetAsync($"/api/v1/screening/matches/{matchId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await other.AdminClient.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/dismiss", new { reason = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await other.AdminClient.PostAsJsonAsync($"/api/v1/screening/matches/{matchId}/confirm", new { reason = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.AdminClient.PostAsync($"/api/v1/customers/{customerId}/screen", null)).StatusCode);
            var list = await other.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/screening/matches?customerId={customerId}");
            Assert.Equal(0, list.GetProperty("totalCount").GetInt32());

            Assert.Equal(HttpStatusCode.OK,
                (await UploadAsync("reference,name,category,kind\nPEP-1,Someone Else,PEP,INDIVIDUAL\n")).StatusCode);
            var otherLists = await other.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/screening/lists");
            Assert.Equal(0, otherLists.GetProperty("tenantList").GetProperty("entryCount").GetInt32());
            Assert.Equal(0, (await other.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/dashboard"))
                .GetProperty("screening").GetProperty("pendingReview").GetInt32());
        }
        finally
        {
            await other.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dashboard_counts_pending_matches()
    {
        await CreateIndividualAsync("Zephaniah", "Mbwana", null);
        var dashboard = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/dashboard");
        Assert.True(dashboard.GetProperty("screening").GetProperty("pendingReview").GetInt32() >= 1);
    }

    private async Task<Guid> CreateIndividualAsync(string first, string last, string? dateOfBirth)
    {
        var response = await _fx.AdminClient.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL", country = "KE", firstName = first, lastName = last, dateOfBirth
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<JsonElement> MatchesAsync(Guid customerId)
        => _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/screening/matches?customerId={customerId}");

    private async Task<JsonElement> SingleMatchAsync(Guid customerId)
        => Assert.Single((await MatchesAsync(customerId)).GetProperty("items").EnumerateArray());

    private Task<HttpResponseMessage> UploadAsync(string csv)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "list.csv" }
        };
        return _fx.AdminClient.PutAsync("/api/v1/screening/tenant-list", form);
    }
}
