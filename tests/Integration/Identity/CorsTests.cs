namespace Aegis.Tests.Integration.Identity;

public sealed class CorsTests : IDisposable
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private readonly AegisApiFactory _factory = new(ConnectionString, new Dictionary<string, string?>
    {
        ["Cors:AllowedOrigins:0"] = "http://localhost:3000"
    });

    public void Dispose() => _factory.Dispose();

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Allowed_origin_gets_cors_header()
    {
        var response = await PreflightAsync("http://localhost:3000");
        Assert.Equal("http://localhost:3000",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Unknown_origin_gets_no_cors_header()
    {
        var response = await PreflightAsync("https://evil.example");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
