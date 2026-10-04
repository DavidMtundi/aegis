namespace Aegis.Application.Screening;

using Aegis.Modules.Screening.Domain;

/// <summary>The current UN and OFAC subjects, prepared for screening and reloaded when a list changes.</summary>
public interface IGlobalWatchlist
{
    Task<WatchlistIndex> GetAsync(CancellationToken cancellationToken = default);
}
