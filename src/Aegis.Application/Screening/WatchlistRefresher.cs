namespace Aegis.Application.Screening;

using System.Security.Cryptography;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Microsoft.Extensions.Logging;

public enum WatchlistRefreshStatus { LOADED, UNCHANGED, FAILED }

public sealed record WatchlistRefreshResult(WatchlistSource Source, WatchlistRefreshStatus Status, int EntryCount, string? Error);

public interface IWatchlistDownloader
{
    Task<byte[]> DownloadAsync(WatchlistSource source, CancellationToken cancellationToken);
}

public interface IWatchlistRefresher
{
    IReadOnlyList<WatchlistSource> Sources { get; }

    Task<WatchlistRefreshResult> RefreshAsync(WatchlistSource source, CancellationToken cancellationToken = default);
}

public sealed class WatchlistRefresher : IWatchlistRefresher
{
    /// <summary>A new file with less than this share of the previous entry count is treated as truncated.</summary>
    public const double MinimumRetainedShare = 0.5;

    private readonly IWatchlistDownloader _downloader;
    private readonly IWatchlistRepository _lists;
    private readonly ILogger<WatchlistRefresher> _logger;

    public WatchlistRefresher(IWatchlistDownloader downloader, IWatchlistRepository lists, ILogger<WatchlistRefresher> logger)
    {
        _downloader = downloader;
        _lists = lists;
        _logger = logger;
    }

    public IReadOnlyList<WatchlistSource> Sources { get; } = new[] { WatchlistSource.UN, WatchlistSource.OFAC };

    public async Task<WatchlistRefreshResult> RefreshAsync(WatchlistSource source, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await _downloader.DownloadAsync(source, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var latest = await _lists.GetLatestLoadAsync(source, cancellationToken);
            if (latest?.ContentHash == hash)
                return new WatchlistRefreshResult(source, WatchlistRefreshStatus.UNCHANGED, latest.EntryCount, null);

            using var stream = new MemoryStream(bytes);
            var parsed = source switch
            {
                WatchlistSource.UN => UnSanctionsParser.Parse(stream),
                WatchlistSource.OFAC => OfacSdnParser.Parse(stream),
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Only UN and OFAC are downloaded.")
            };

            var previous = latest?.EntryCount ?? 0;
            if (parsed.Subjects.Count == 0 || parsed.Subjects.Count < previous * MinimumRetainedShare)
                return Failed(source, $"Rejected: {parsed.Subjects.Count} entries against {previous} in the current list.");

            await _lists.ReplaceSourceAsync(
                WatchlistLoad.Record(source, parsed.PublishedAt, parsed.Subjects.Count, hash), parsed.Subjects, cancellationToken);
            _logger.LogInformation("Loaded {Count} {Source} watchlist entries", parsed.Subjects.Count, source);
            return new WatchlistRefreshResult(source, WatchlistRefreshStatus.LOADED, parsed.Subjects.Count, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Refreshing the {Source} watchlist failed", source);
            return Failed(source, ex.Message);
        }
    }

    private WatchlistRefreshResult Failed(WatchlistSource source, string error)
    {
        _logger.LogWarning("Watchlist {Source} not loaded: {Error}", source, error);
        return new WatchlistRefreshResult(source, WatchlistRefreshStatus.FAILED, 0, error);
    }
}
