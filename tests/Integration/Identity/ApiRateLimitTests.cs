namespace Aegis.Tests.Integration.Identity;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class ApiRateLimitTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString, new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "3",
            ["RateLimiting:Api:WindowSeconds"] = "60"
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Fourth_authenticated_api_call_in_window_is_rejected_with_429()
    {
        var slug = $"rl-{Guid.NewGuid():N}"[..16];
        var email = $"admin@{slug}.test";
        var bootstrap = await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Rate Limit Tenant",
            slug,
            adminEmail = email,
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Passw0rd!",
            tenantSlug = slug
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (var i = 0; i < 3; i++)
        {
            var ok = await authed.GetAsync("/api/v1/status");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var limited = await authed.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
