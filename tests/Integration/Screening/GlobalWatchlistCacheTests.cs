namespace Aegis.Tests.Integration.Screening;

using Aegis.Application.Screening;
using Aegis.Infrastructure.Screening;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Microsoft.Extensions.DependencyInjection;

public sealed class GlobalWatchlistCacheTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task The_snapshot_version_names_the_loads_its_index_was_built_from()
    {
        var lists = new FakeLists();
        var load = lists.Load(WatchlistSource.UN, Now, "ZEPHANIAH OKOTH MBWANA");

        var snapshot = await Cache(lists).GetAsync();

        Assert.Equal(ScreeningListVersion.Global(new[] { load }), snapshot.Version);
        Assert.Equal(1, snapshot.Index.Count);
    }

    [Fact]
    public async Task Unchanged_loads_reuse_the_index()
    {
        var lists = new FakeLists();
        lists.Load(WatchlistSource.UN, Now, "ZEPHANIAH OKOTH MBWANA");
        var cache = Cache(lists);

        var first = await cache.GetAsync();
        var second = await cache.GetAsync();

        Assert.Same(first.Index, second.Index);
        Assert.Equal(1, lists.SubjectReads);
    }

    [Fact]
    public async Task A_new_load_id_rebuilds_the_index_even_when_its_load_time_is_older()
    {
        var lists = new FakeLists();
        lists.Load(WatchlistSource.UN, Now, "ZEPHANIAH OKOTH MBWANA");
        var cache = Cache(lists);
        var first = await cache.GetAsync();

        var replacement = lists.Load(WatchlistSource.UN, Now.AddHours(-1), "ZEPHANIAH OKOTH MBWANA", "BARAKA JUMA");
        var second = await cache.GetAsync();

        Assert.NotEqual(first.Version, second.Version);
        Assert.Equal(ScreeningListVersion.Global(new[] { replacement }), second.Version);
        Assert.Equal(2, second.Index.Count);
    }

    private static GlobalWatchlistCache Cache(FakeLists lists)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWatchlistRepository>(lists);
        return new GlobalWatchlistCache(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>The newest load per source is whichever was set last, regardless of its timestamp.</summary>
    private sealed class FakeLists : IWatchlistRepository
    {
        private readonly Dictionary<WatchlistSource, (WatchlistLoad Load, WatchlistSubject[] Subjects)> _current = new();

        public int SubjectReads { get; private set; }

        public WatchlistLoad Load(WatchlistSource source, DateTimeOffset loadedAt, params string[] names)
        {
            var load = WatchlistLoad.Record(source, null, names.Length, Guid.NewGuid().ToString("N"), loadedAt);
            _current[source] = (load, names.Select((n, i) => new WatchlistSubject(
                source, $"{source}-{i}", WatchlistCategory.SANCTIONS, SubjectKind.INDIVIDUAL, new[] { n },
                Array.Empty<BirthYearRange>(), Array.Empty<string>(), null)).ToArray());
            return load;
        }

        public Task<IReadOnlyList<WatchlistLoad>> ListLatestLoadsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WatchlistLoad>>(_current.Values.Select(v => v.Load).ToList());

        public Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(CancellationToken cancellationToken = default)
        {
            SubjectReads++;
            return Task.FromResult<IReadOnlyList<WatchlistSubject>>(_current.Values.SelectMany(v => v.Subjects).ToList());
        }

        public Task<WatchlistLoad?> GetLatestLoadAsync(WatchlistSource source, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MarkCheckedAsync(Guid loadId, DateTimeOffset checkedAt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReplaceSourceAsync(WatchlistLoad load, IReadOnlyList<WatchlistSubject> subjects, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
