namespace Aegis.Tests.EndToEnd.Support;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

public sealed class AegisApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public AegisApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Aegis"] = _connectionString,
                ["Jwt:SigningKey"] = "dev-only-signing-key-change-me-32chars-min!!",
                ["Jwt:Issuer"] = "aegis",
                ["Jwt:Audience"] = "aegis-api",
                ["Aegis:AllowDevBootstrap"] = "true"
            });
        });
    }
}
