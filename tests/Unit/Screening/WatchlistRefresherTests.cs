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
        var refresher = Refresher(new FakeDownloader(Fixture("un-sample.xml")), repo);

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
        var clock = new MutableClock();
        var refresher = Refresher(new FakeDownloader(Fixture("un-sample.xml")), repo, clock);
        await refresher.RefreshAsync(WatchlistSource.UN);
        clock.Advance(WatchlistRefresher.Cooldown + TimeSpan.FromMinutes(1));

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.UNCHANGED, result.Status);
        Assert.Single(repo.Loads);
    }

    [Fact]
    public async Task A_file_with_far_fewer_entries_is_rejected_as_truncated()
    {
        var repo = new FakeWatchlistRepository();
        repo.Loads.Add(WatchlistLoad.Record(WatchlistSource.UN, null, 1000, "previous"));
        var clock = new MutableClock();
        clock.Advance(WatchlistRefresher.Cooldown + TimeSpan.FromMinutes(1));
        var refresher = Refresher(new FakeDownloader(Fixture("un-sample.xml")), repo, clock);

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.FAILED, result.Status);
        Assert.Contains("4 entries", result.Error);
        Assert.Empty(repo.Subjects);
    }

    [Fact]
    public async Task Download_or_parse_failure_is_reported_not_thrown()
    {
        var refresher = Refresher(new FakeDownloader("<html/>"u8.ToArray()), new FakeWatchlistRepository());

        var result = await refresher.RefreshAsync(WatchlistSource.OFAC);

        Assert.Equal(WatchlistRefreshStatus.FAILED, result.Status);
        Assert.Equal("Not an OFAC SDN list.", result.Error);
    }

    [Fact]
    public async Task Download_timeout_without_caller_cancellation_is_reported_as_failure()
    {
        var refresher = Refresher(new ThrowingDownloader(new TaskCanceledException("timed out")), new FakeWatchlistRepository());

        var result = await refresher.RefreshAsync(WatchlistSource.UN, CancellationToken.None);

        Assert.Equal(WatchlistRefreshStatus.FAILED, result.Status);
        Assert.Equal("timed out", result.Error);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var refresher = Refresher(new ThrowingDownloader(new TaskCanceledException()), new FakeWatchlistRepository());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresher.RefreshAsync(WatchlistSource.UN, cts.Token));
    }

    [Fact]
    public async Task A_list_with_no_entries_is_rejected()
    {
        var repo = new FakeWatchlistRepository();
        var refresher = Refresher(new FakeDownloader("<CONSOLIDATED_LIST><INDIVIDUALS/><ENTITIES/></CONSOLIDATED_LIST>"u8.ToArray()), repo);

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.FAILED, result.Status);
        Assert.Contains("0 entries", result.Error);
        Assert.Empty(repo.Loads);
    }

    [Fact]
    public async Task A_source_attempted_within_the_cooldown_is_skipped_without_downloading()
    {
        var repo = new FakeWatchlistRepository();
        var downloader = new FakeDownloader(Fixture("un-sample.xml"));
        var clock = new MutableClock();
        var refresher = Refresher(downloader, repo, clock);
        await refresher.RefreshAsync(WatchlistSource.UN);
        clock.Advance(TimeSpan.FromMinutes(5));

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.SKIPPED, result.Status);
        Assert.Equal(4, result.EntryCount);
        Assert.Equal(1, downloader.Calls);
    }

    [Fact]
    public async Task An_unchanged_check_restarts_the_cooldown()
    {
        var repo = new FakeWatchlistRepository();
        var downloader = new FakeDownloader(Fixture("un-sample.xml"));
        var clock = new MutableClock();
        var refresher = Refresher(downloader, repo, clock);
        await refresher.RefreshAsync(WatchlistSource.UN);
        clock.Advance(WatchlistRefresher.Cooldown + TimeSpan.FromMinutes(1));
        Assert.Equal(WatchlistRefreshStatus.UNCHANGED, (await refresher.RefreshAsync(WatchlistSource.UN)).Status);
        clock.Advance(TimeSpan.FromMinutes(5));

        var result = await refresher.RefreshAsync(WatchlistSource.UN);

        Assert.Equal(WatchlistRefreshStatus.SKIPPED, result.Status);
        Assert.Equal(2, downloader.Calls);
    }

    [Fact]
    public async Task Refreshing_all_sources_while_another_refresh_runs_returns_null()
    {
        var downloader = new FakeDownloader(Fixture("un-sample.xml"));
        var refresher = Refresher(downloader, new FakeWatchlistRepository(), refreshLock: new FakeLock(available: false));

        Assert.Null(await refresher.RefreshAllAsync());
        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public async Task Refreshing_all_sources_holds_the_lock_until_done()
    {
        var refreshLock = new FakeLock(available: true);
        var refresher = Refresher(new FakeDownloader(Fixture("un-sample.xml")), new FakeWatchlistRepository(), refreshLock: refreshLock);

        var results = await refresher.RefreshAllAsync();

        Assert.NotNull(results);
        Assert.Equal(new[] { WatchlistSource.UN, WatchlistSource.OFAC }, results!.Select(r => r.Source));
        Assert.Equal(1, refreshLock.Acquired);
        Assert.Equal(1, refreshLock.Released);
    }

    private static WatchlistRefresher Refresher(
        IWatchlistDownloader downloader, IWatchlistRepository repo, TimeProvider? clock = null, IWatchlistRefreshLock? refreshLock = null)
        => new(downloader, repo, refreshLock ?? new FakeLock(available: true), NullLogger<WatchlistRefresher>.Instance, clock ?? TimeProvider.System);

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeLock(bool available) : IWatchlistRefreshLock
    {
        public int Acquired { get; private set; }
        public int Released { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken = default)
        {
            if (!available) return Task.FromResult<IAsyncDisposable?>(null);
            Acquired++;
            return Task.FromResult<IAsyncDisposable?>(new Handle(this));
        }

        private sealed class Handle(FakeLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ThrowingDownloader(Exception exception) : IWatchlistDownloader
    {
        public Task<byte[]> DownloadAsync(WatchlistSource source, CancellationToken cancellationToken) => Task.FromException<byte[]>(exception);
    }

    private sealed class FakeDownloader(byte[] bytes) : IWatchlistDownloader
    {
        public int Calls { get; private set; }

        public Task<byte[]> DownloadAsync(WatchlistSource source, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(bytes);
        }
    }

    private sealed class FakeWatchlistRepository : IWatchlistRepository
    {
        public List<WatchlistLoad> Loads { get; } = new();
        public List<WatchlistSubject> Subjects { get; } = new();

        public Task<IReadOnlyList<WatchlistLoad>> ListLatestLoadsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WatchlistLoad>>(Loads);

        public Task<WatchlistLoad?> GetLatestLoadAsync(WatchlistSource source, CancellationToken cancellationToken = default)
            => Task.FromResult(Loads.Where(l => l.Source == source).MaxBy(l => l.LoadedAt));

        public Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WatchlistSubject>>(Subjects);

        public Task MarkCheckedAsync(Guid loadId, DateTimeOffset checkedAt, CancellationToken cancellationToken = default)
        {
            Loads.Single(l => l.Id == loadId).MarkChecked(checkedAt);
            return Task.CompletedTask;
        }

        public Task ReplaceSourceAsync(WatchlistLoad load, IReadOnlyList<WatchlistSubject> subjects, CancellationToken cancellationToken = default)
        {
            Subjects.RemoveAll(s => s.Source == load.Source);
            Subjects.AddRange(subjects);
            Loads.Add(load);
            return Task.CompletedTask;
        }
    }
}
