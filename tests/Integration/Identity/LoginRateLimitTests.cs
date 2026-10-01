namespace Aegis.Tests.Integration.Identity;

using System.Net;
using System.Net.Http.Json;

public sealed class LoginRateLimitTests : IDisposable
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private readonly AegisApiFactory _factory = new(ConnectionString, new Dictionary<string, string?>
    {
        ["RateLimiting:Login:PermitLimit"] = "3",
        ["RateLimiting:Login:WindowSeconds"] = "60"
    });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Fourth_login_attempt_in_window_is_rejected_with_429()
    {
        using var client = _factory.CreateClient();
        var body = new { email = "nobody@x.test", password = "wrong", tenantSlug = "no-such-tenant" };

        for (var i = 0; i < 3; i++)
        {
            var r = await client.PostAsJsonAsync("/api/v1/auth/login", body);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
