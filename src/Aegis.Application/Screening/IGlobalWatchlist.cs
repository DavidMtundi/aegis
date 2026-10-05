namespace Aegis.Application.Screening;

using Aegis.Modules.Screening.Domain;

/// <summary>The UN and OFAC index and the global list version (<see cref="Modules.Screening.Application.ScreeningListVersion.Global"/>) it was built from.</summary>
public sealed record GlobalWatchlistSnapshot(WatchlistIndex Index, string Version);

/// <summary>The current UN and OFAC subjects, prepared for screening and reloaded when a list changes.</summary>
public interface IGlobalWatchlist
{
    Task<GlobalWatchlistSnapshot> GetAsync(CancellationToken cancellationToken = default);
}
