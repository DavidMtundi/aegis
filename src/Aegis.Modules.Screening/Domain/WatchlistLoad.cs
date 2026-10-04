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

    private WatchlistLoad() { }

    public static WatchlistLoad Record(WatchlistSource source, DateTimeOffset? publishedAt, int entryCount, string contentHash) => new()
    {
        Id = Guid.NewGuid(),
        Source = source,
        LoadedAt = DateTimeOffset.UtcNow,
        PublishedAt = publishedAt,
        EntryCount = entryCount,
        ContentHash = contentHash
    };
}
