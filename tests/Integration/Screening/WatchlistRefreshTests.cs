namespace Aegis.Tests.Integration.Screening;

using Aegis.Application.Screening;
using Aegis.Modules.Screening.Domain;
using Aegis.Tests.Integration.Alerts;
using Microsoft.Extensions.DependencyInjection;

public sealed class WatchlistRefreshTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Fixture_lists_load_and_feed_the_global_index()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var refresher = scope.ServiceProvider.GetRequiredService<IWatchlistRefresher>();

        // The global tables are shared by every test class, so a previous test may already have loaded them.
        var un = await refresher.RefreshAsync(WatchlistSource.UN);
        var ofac = await refresher.RefreshAsync(WatchlistSource.OFAC);
        Assert.Contains(un.Status, new[] { WatchlistRefreshStatus.LOADED, WatchlistRefreshStatus.UNCHANGED, WatchlistRefreshStatus.SKIPPED });
        Assert.Equal(4, un.EntryCount);
        Assert.Equal(2, ofac.EntryCount);
        Assert.Equal(WatchlistRefreshStatus.SKIPPED, (await refresher.RefreshAsync(WatchlistSource.UN)).Status);

        var index = await scope.ServiceProvider.GetRequiredService<IGlobalWatchlist>().GetAsync();
        Assert.Equal(6, index.Count);
    }
}
