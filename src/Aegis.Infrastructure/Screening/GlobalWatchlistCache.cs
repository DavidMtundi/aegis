namespace Aegis.Infrastructure.Screening;

using Aegis.Application.Screening;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Singleton. Each call reads the newest load id per source (one cheap query) and rebuilds the index only when
/// one changed, so every API instance picks up a refresh done by another.
/// </summary>
public sealed class GlobalWatchlistCache : IGlobalWatchlist
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private GlobalWatchlistSnapshot _current = new(new WatchlistIndex(Array.Empty<WatchlistSubject>()), "");
    private bool _built;

    public GlobalWatchlistCache(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<GlobalWatchlistSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var lists = scope.ServiceProvider.GetRequiredService<IWatchlistRepository>();
        // Version before subjects: a refresh in between gives a newer index under an older version, which only
        // causes an extra rescreen, never a tenant marked screened against lists the index did not contain.
        var version = ScreeningListVersion.Global(await lists.ListLatestLoadsAsync(cancellationToken));
        var current = Volatile.Read(ref _current);
        if (Volatile.Read(ref _built) && version == current.Version) return current;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            current = Volatile.Read(ref _current);
            if (_built && version == current.Version) return current;
            current = new GlobalWatchlistSnapshot(new WatchlistIndex(await lists.ListSubjectsAsync(cancellationToken)), version);
            Volatile.Write(ref _current, current);
            Volatile.Write(ref _built, true);
            return current;
        }
        finally
        {
            _lock.Release();
        }
    }
}
