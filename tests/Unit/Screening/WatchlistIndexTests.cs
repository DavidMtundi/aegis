namespace Aegis.Tests.Unit.Screening;

using Aegis.Modules.Screening.Domain;

public sealed class WatchlistIndexTests
{
    private static readonly WatchlistSubject Listed = new(
        WatchlistSource.UN, "QDi.901", WatchlistCategory.SANCTIONS, SubjectKind.INDIVIDUAL,
        new[] { "ZEPHANIAH OKOTH MBWANA", "Zeph Mbwana" },
        new[] { new BirthYearRange(1978, 1978) },
        new[] { "Kenya" },
        "Al-Qaida");

    private static readonly WatchlistSubject Company = new(
        WatchlistSource.OFAC, "990002", WatchlistCategory.SANCTIONS, SubjectKind.ENTITY,
        new[] { "SILVERLINE LOGISTICS FZE" }, Array.Empty<BirthYearRange>(), Array.Empty<string>(), "IRAN");

    private readonly WatchlistIndex _index = new(new[] { Listed, Company });

    [Fact]
    public void Matches_on_alias_and_reports_the_best_name()
    {
        var match = Assert.Single(_index.Screen(Candidate("Zephaniah Mbwana")));
        Assert.Equal("QDi.901", match.Subject.ExternalId);
        Assert.True(match.Score >= WatchlistIndex.DefaultThreshold);
        Assert.False(match.BirthYearMatched);
    }

    [Fact]
    public void Birth_year_inside_tolerance_is_flagged()
        => Assert.True(Assert.Single(_index.Screen(Candidate("Zephaniah Mbwana", new DateOnly(1979, 3, 1)))).BirthYearMatched);

    [Fact]
    public void Birth_year_outside_the_listed_range_excludes_the_subject()
        => Assert.Empty(_index.Screen(Candidate("Zephaniah Mbwana", new DateOnly(1990, 1, 1))));

    [Fact]
    public void Kind_must_agree()
        => Assert.Empty(_index.Screen(new ScreeningCandidate(Guid.NewGuid(), "Zephaniah Mbwana", SubjectKind.ENTITY, null)));

    [Fact]
    public void Business_matches_entity_despite_suffix()
        => Assert.Equal("990002", Assert.Single(_index.Screen(
            new ScreeningCandidate(Guid.NewGuid(), "Silverline Logistics FZE Limited", SubjectKind.ENTITY, null))).Subject.ExternalId);

    [Fact]
    public void Unrelated_name_does_not_match()
        => Assert.Empty(_index.Screen(Candidate("Grace Akinyi")));

    private static ScreeningCandidate Candidate(string name, DateOnly? dob = null)
        => new(Guid.NewGuid(), name, SubjectKind.INDIVIDUAL, dob);
}
