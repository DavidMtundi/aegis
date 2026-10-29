namespace Aegis.Tests.Integration.Identity;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class RefreshTokenFlowTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
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
    public async Task Login_refresh_rotates_and_logout_revokes()
    {
        var slug = $"rt-{Guid.NewGuid():N}"[..18];
        var bootstrap = await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Refresh Tenant",
            slug,
            adminEmail = "admin-rt@example.com",
            adminName = "Admin RT",
            adminPassword = "Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin-rt@example.com",
            password = "Passw0rd!",
            tenantSlug = slug
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var access1 = loginBody.GetProperty("accessToken").GetString();
        var refresh1 = loginBody.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(access1));
        Assert.False(string.IsNullOrWhiteSpace(refresh1));

        var refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshBody = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        var access2 = refreshBody.GetProperty("accessToken").GetString();
        var refresh2 = refreshBody.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(access2));
        Assert.NotEqual(refresh1, refresh2);

        var reuse = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var logout = await _client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = refresh2 });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh2 });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
