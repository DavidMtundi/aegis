namespace Aegis.Application.Screening;

using System.Security.Cryptography;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Microsoft.Extensions.Logging;

/// <summary>SKIPPED: the source was attempted within <see cref="WatchlistRefresher.Cooldown"/>, so it was not downloaded.</summary>
public enum WatchlistRefreshStatus { LOADED, UNCHANGED, FAILED, SKIPPED }

public sealed record WatchlistRefreshResult(WatchlistSource Source, WatchlistRefreshStatus Status, int EntryCount, string? Error);

public interface IWatchlistDownloader
{
    Task<byte[]> DownloadAsync(WatchlistSource source, CancellationToken cancellationToken);
}

/// <summary>One global list refresh at a time, across every API instance.</summary>
public interface IWatchlistRefreshLock
{
    /// <returns>A handle that releases the lock when disposed, or null when another refresh holds it.</returns>
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken = default);
}

public interface IWatchlistRefresher
{
    IReadOnlyList<WatchlistSource> Sources { get; }

    Task<WatchlistRefreshResult> RefreshAsync(WatchlistSource source, CancellationToken cancellationToken = default);

    /// <summary>Refreshes every source under <see cref="IWatchlistRefreshLock"/>.</summary>
    /// <param name="onlyMissing">Only sources that have never been loaded.</param>
    /// <returns>Null when another refresh is already running.</returns>
    Task<IReadOnlyList<WatchlistRefreshResult>?> RefreshAllAsync(bool onlyMissing = false, CancellationToken cancellationToken = default);
}

public sealed class WatchlistRefresher : IWatchlistRefresher
{
    /// <summary>A new file with less than this share of the previous entry count is treated as truncated.</summary>
    public const double MinimumRetainedShare = 0.5;

    /// <summary>A source attempted more recently than this is not downloaded again.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    private readonly IWatchlistDownloader _downloader;
    private readonly IWatchlistRepository _lists;
    private readonly IWatchlistRefreshLock _lock;
    private readonly ILogger<WatchlistRefresher> _logger;
    private readonly TimeProvider _clock;

    public WatchlistRefresher(
        IWatchlistDownloader downloader, IWatchlistRepository lists, IWatchlistRefreshLock refreshLock,
        ILogger<WatchlistRefresher> logger, TimeProvider? clock = null)
    {
        _downloader = downloader;
        _lists = lists;
        _lock = refreshLock;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<WatchlistSource> Sources { get; } = new[] { WatchlistSource.UN, WatchlistSource.OFAC };

    public async Task<IReadOnlyList<WatchlistRefreshResult>?> RefreshAllAsync(
        bool onlyMissing = false, CancellationToken cancellationToken = default)
    {
        await using var held = await _lock.TryAcquireAsync(cancellationToken);
        if (held is null) return null;

        var results = new List<WatchlistRefreshResult>();
        foreach (var source in Sources)
        {
            if (onlyMissing && await _lists.GetLatestLoadAsync(source, cancellationToken) is not null) continue;
            results.Add(await RefreshAsync(source, cancellationToken));
        }
        return results;
    }

    public async Task<WatchlistRefreshResult> RefreshAsync(WatchlistSource source, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        WatchlistLoad? latest = null;
        try
        {
            latest = await _lists.GetLatestLoadAsync(source, cancellationToken);
            if (latest is not null && now - latest.LastAttemptAt < Cooldown)
                return new WatchlistRefreshResult(source, WatchlistRefreshStatus.SKIPPED, latest.EntryCount, null);

            var bytes = await _downloader.DownloadAsync(source, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (latest?.ContentHash == hash)
            {
                await _lists.MarkCheckedAsync(latest.Id, now, cancellationToken);
                return new WatchlistRefreshResult(source, WatchlistRefreshStatus.UNCHANGED, latest.EntryCount, null);
            }

            using var stream = new MemoryStream(bytes);
            var parsed = source switch
            {
                WatchlistSource.UN => UnSanctionsParser.Parse(stream),
                WatchlistSource.OFAC => OfacSdnParser.Parse(stream),
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Only UN and OFAC are downloaded.")
            };

            var previous = latest?.EntryCount ?? 0;
            if (parsed.Subjects.Count == 0 || parsed.Subjects.Count < previous * MinimumRetainedShare)
            {
                await MarkCheckedAsync(latest, now);
                return Failed(source, $"Rejected: {parsed.Subjects.Count} entries against {previous} in the current list.");
            }

            await _lists.ReplaceSourceAsync(
                WatchlistLoad.Record(source, parsed.PublishedAt, parsed.Subjects.Count, hash, now), parsed.Subjects, cancellationToken);
            _logger.LogInformation("Loaded {Count} {Source} watchlist entries", parsed.Subjects.Count, source);
            return new WatchlistRefreshResult(source, WatchlistRefreshStatus.LOADED, parsed.Subjects.Count, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Refreshing the {Source} watchlist failed", source);
            await MarkCheckedAsync(latest, now);
            return Failed(source, ex.Message);
        }
    }

    /// <summary>A failed attempt also starts the cooldown, so a broken upstream is not hammered.</summary>
    private async Task MarkCheckedAsync(WatchlistLoad? latest, DateTimeOffset now)
    {
        if (latest is null) return;
        try
        {
            await _lists.MarkCheckedAsync(latest.Id, now, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recording the {Source} watchlist attempt failed", latest.Source);
        }
    }

    private WatchlistRefreshResult Failed(WatchlistSource source, string error)
    {
        _logger.LogWarning("Watchlist {Source} not loaded: {Error}", source, error);
        return new WatchlistRefreshResult(source, WatchlistRefreshStatus.FAILED, 0, error);
    }
}
