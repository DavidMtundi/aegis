namespace Aegis.Modules.Screening.Domain;

/// <summary>A UN or OFAC subject. Shared by every tenant, so it is not tenant-owned.</summary>
public sealed class WatchlistEntry
{
    public Guid Id { get; private set; }
    public WatchlistSource Source { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public WatchlistCategory Category { get; private set; }
    public SubjectKind Kind { get; private set; }
    public List<string> Names { get; private set; } = new();
    public List<BirthYearRange> BirthYears { get; private set; } = new();
    public List<string> Countries { get; private set; } = new();
    public string? Detail { get; private set; }
    public Guid LoadId { get; private set; }

    private WatchlistEntry() { }

    public static WatchlistEntry From(WatchlistSubject s, Guid loadId) => new()
    {
        Id = Guid.NewGuid(),
        Source = s.Source,
        ExternalId = s.ExternalId,
        Category = s.Category,
        Kind = s.Kind,
        Names = s.Names.ToList(),
        BirthYears = s.BirthYears.ToList(),
        Countries = s.Countries.ToList(),
        Detail = s.Detail,
        LoadId = loadId
    };

    public WatchlistSubject ToSubject() => new(Source, ExternalId, Category, Kind, Names, BirthYears, Countries, Detail);
}
