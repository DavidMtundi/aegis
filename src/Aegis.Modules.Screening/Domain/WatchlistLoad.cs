namespace Aegis.Modules.Screening.Domain;

/// <summary>One successful load of a global list; the newest row per source is the current list.</summary>
public sealed class WatchlistLoad
{
    public Guid Id { get; private set; }
    public WatchlistSource Source { get; private set; }
    public DateTimeOffset LoadedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public int EntryCount { get; private set; }
    public string ContentHash { get; private set; } = null!;

    /// <summary>Last time the source was downloaded again and found unchanged or unusable.</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>Newest download attempt that ended with this load still current.</summary>
    public DateTimeOffset LastAttemptAt => CheckedAt is { } checkedAt && checkedAt > LoadedAt ? checkedAt : LoadedAt;

    private WatchlistLoad() { }

    public static WatchlistLoad Record(
        WatchlistSource source, DateTimeOffset? publishedAt, int entryCount, string contentHash, DateTimeOffset? loadedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Source = source,
        LoadedAt = loadedAt ?? DateTimeOffset.UtcNow,
        PublishedAt = publishedAt,
        EntryCount = entryCount,
        ContentHash = contentHash
    };

    public void MarkChecked(DateTimeOffset checkedAt) => CheckedAt = checkedAt;
}
