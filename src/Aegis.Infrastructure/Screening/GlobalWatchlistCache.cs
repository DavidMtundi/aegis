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
    private (DateTimeOffset? LoadedAt, WatchlistIndex Index) _current = (null, new WatchlistIndex(Array.Empty<WatchlistSubject>()));

    public GlobalWatchlistCache(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<WatchlistIndex> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var lists = scope.ServiceProvider.GetRequiredService<IWatchlistRepository>();
        var loadedAt = await lists.GetLastLoadedAtAsync(cancellationToken);
        if (loadedAt == _current.LoadedAt) return _current.Index;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (loadedAt != _current.LoadedAt)
                _current = (loadedAt, new WatchlistIndex(await lists.ListSubjectsAsync(cancellationToken)));
            return _current.Index;
        }
        finally
        {
            _lock.Release();
        }
    }
}
