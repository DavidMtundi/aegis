namespace Aegis.Modules.Screening.Domain;

public enum WatchlistSource { UN, OFAC, TENANT }

public enum WatchlistCategory { SANCTIONS, PEP, WATCHLIST }

public enum SubjectKind { INDIVIDUAL, ENTITY }

/// <summary>Inclusive range of possible birth years; lists often give only a year or a span.</summary>
public sealed record BirthYearRange(int From, int To)
{
    public bool Contains(int year, int tolerance) => year >= From - tolerance && year <= To + tolerance;
}

/// <summary>One listed person or organisation. <see cref="Names"/>[0] is the primary name; the rest are aliases.</summary>
public sealed record WatchlistSubject(
    WatchlistSource Source,
    string ExternalId,
    WatchlistCategory Category,
    SubjectKind Kind,
    IReadOnlyList<string> Names,
    IReadOnlyList<BirthYearRange> BirthYears,
    IReadOnlyList<string> Countries,
    string? Detail);

/// <summary>The customer being screened. Businesses screen as <see cref="SubjectKind.ENTITY"/>.</summary>
public sealed record ScreeningCandidate(Guid CustomerId, string Name, SubjectKind Kind, DateOnly? DateOfBirth);

/// <param name="BirthYearConflict">The customer's birth year is outside the listed range, kept only because the name is near exact.</param>
public sealed record PotentialMatch(
    WatchlistSubject Subject, string MatchedName, double Score, bool BirthYearMatched, bool BirthYearConflict = false);
