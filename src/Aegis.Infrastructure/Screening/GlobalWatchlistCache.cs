namespace Aegis.Infrastructure.Screening;

using Aegis.Application.Screening;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Singleton. Each call checks the newest load time (one cheap query) and rebuilds the index only when
/// a list changed, so every API instance picks up a refresh done by another.
/// </summary>
public sealed class GlobalWatchlistCache : IGlobalWatchlist
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Snapshot _current = new(null, new WatchlistIndex(Array.Empty<WatchlistSubject>()));

    public GlobalWatchlistCache(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<WatchlistIndex> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var lists = scope.ServiceProvider.GetRequiredService<IWatchlistRepository>();
        var loadedAt = await lists.GetLastLoadedAtAsync(cancellationToken);
        var current = Volatile.Read(ref _current);
        if (loadedAt == current.LoadedAt) return current.Index;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            current = Volatile.Read(ref _current);
            if (loadedAt == current.LoadedAt) return current.Index;
            current = new Snapshot(loadedAt, new WatchlistIndex(await lists.ListSubjectsAsync(cancellationToken)));
            Volatile.Write(ref _current, current);
            return current.Index;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record Snapshot(DateTimeOffset? LoadedAt, WatchlistIndex Index);
}
