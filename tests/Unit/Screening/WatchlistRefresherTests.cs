namespace Aegis.Tests.Unit.Screening;

using Aegis.Application.Screening;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class WatchlistRefresherTests
{
    private static byte[] Fixture(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "screening", name));

    [Fact]
    public async Task First_load_replaces_the_source()
    {
        var repo = new FakeWatchlistRepository();
        var refresher = new WatchlistRefresher(new FakeDownloader(Fixture("un-sample.xml")), repo, NullLogger<WatchlistRefresher>.Instance);

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.LOADED, result.Status);
        Assert.Equal(4, result.EntryCount);
        Assert.Equal(4, repo.Subjects.Count);
        Assert.Equal(4, Assert.Single(repo.Loads).EntryCount);
    }

    [Fact]
    public async Task Same_file_again_is_unchanged()
    {
        var repo = new FakeWatchlistRepository();
        var refresher = new WatchlistRefresher(new FakeDownloader(Fixture("un-sample.xml")), repo, NullLogger<WatchlistRefresher>.Instance);
        await refresher.RefreshAsync(WatchlistSource.UN);

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.UNCHANGED, result.Status);
        Assert.Single(repo.Loads);
    }

    [Fact]
    public async Task A_file_with_far_fewer_entries_is_rejected_as_truncated()
    {
        var repo = new FakeWatchlistRepository();
        repo.Loads.Add(WatchlistLoad.Record(WatchlistSource.UN, null, 1000, "previous"));
        var refresher = new WatchlistRefresher(new FakeDownloader(Fixture("un-sample.xml")), repo, NullLogger<WatchlistRefresher>.Instance);

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.FAILED, result.Status);
        Assert.Contains("4 entries", result.Error);
        Assert.Empty(repo.Subjects);
    }

    [Fact]
    public async Task Download_or_parse_failure_is_reported_not_thrown()
    {
        var refresher = new WatchlistRefresher(new FakeDownloader("<html/>"u8.ToArray()), new FakeWatchlistRepository(), NullLogger<WatchlistRefresher>.Instance);

        var result = await refresher.RefreshAsync(WatchlistSource.OFAC);

        Assert.Equal(WatchlistRefreshStatus.FAILED, result.Status);
        Assert.Equal("Not an OFAC SDN list.", result.Error);
    }

    private sealed class FakeDownloader(byte[] bytes) : IWatchlistDownloader
    {
        public Task<byte[]> DownloadAsync(WatchlistSource source, CancellationToken cancellationToken) => Task.FromResult(bytes);
    }

    private sealed class FakeWatchlistRepository : IWatchlistRepository
    {
        public List<WatchlistLoad> Loads { get; } = new();
        public List<WatchlistSubject> Subjects { get; } = new();

        public Task<IReadOnlyList<WatchlistLoad>> ListLatestLoadsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WatchlistLoad>>(Loads);

        public Task<WatchlistLoad?> GetLatestLoadAsync(WatchlistSource source, CancellationToken cancellationToken = default)
            => Task.FromResult(Loads.Where(l => l.Source == source).MaxBy(l => l.LoadedAt));

        public Task<DateTimeOffset?> GetLastLoadedAtAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Loads.Count == 0 ? (DateTimeOffset?)null : Loads.Max(l => l.LoadedAt));

        public Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WatchlistSubject>>(Subjects);

        public Task ReplaceSourceAsync(WatchlistLoad load, IReadOnlyList<WatchlistSubject> subjects, CancellationToken cancellationToken = default)
        {
            Subjects.RemoveAll(s => s.Source == load.Source);
            Subjects.AddRange(subjects);
            Loads.Add(load);
            return Task.CompletedTask;
        }
    }
}
