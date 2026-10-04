namespace Aegis.Tests.Integration.Screening;

using Aegis.Infrastructure.Screening;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class ScreeningRiskFactorSyncServiceTests
{
    [Fact]
    public async Task A_failing_sync_does_not_stop_the_host()
    {
        var service = Service(new InvalidOperationException("database unavailable"));

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Host_shutdown_cancellation_still_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = Service(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync(cts.Token));
    }

    private static ScreeningRiskFactorSyncService Service(Exception failure)
    {
        var services = new ServiceCollection();
        services.AddScoped<ScreeningRiskFactorSync>(_ => throw failure);
        var provider = services.BuildServiceProvider();
        return new ScreeningRiskFactorSyncService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<ScreeningRiskFactorSyncService>.Instance);
    }
}
