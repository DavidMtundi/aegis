namespace Aegis.Modules.Screening.Domain;

using Aegis.Shared.Domain;

public sealed class TenantWatchlistEntry : EntityBase
{
    public string ExternalId { get; private set; } = null!;
    public WatchlistCategory Category { get; private set; }
    public SubjectKind Kind { get; private set; }
    public List<string> Names { get; private set; } = new();
    public List<BirthYearRange> BirthYears { get; private set; } = new();
    public List<string> Countries { get; private set; } = new();
    public string? Detail { get; private set; }
    public string UploadedBy { get; private set; } = null!;

    private TenantWatchlistEntry() { }

    public static TenantWatchlistEntry From(TenantId tenantId, WatchlistSubject s, string uploadedBy)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantWatchlistEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalId = s.ExternalId,
            Category = s.Category,
            Kind = s.Kind,
            Names = s.Names.ToList(),
            BirthYears = s.BirthYears.ToList(),
            Countries = s.Countries.ToList(),
            Detail = s.Detail,
            UploadedBy = uploadedBy,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public WatchlistSubject ToSubject()
        => new(WatchlistSource.TENANT, ExternalId, Category, Kind, Names, BirthYears, Countries, Detail);
}
