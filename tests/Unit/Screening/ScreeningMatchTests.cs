namespace Aegis.Tests.Unit.Screening;

using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;

public sealed class ScreeningMatchTests
{
    private static ScreeningMatch NewMatch()
    {
        var subject = new WatchlistSubject(
            WatchlistSource.UN, "QDi.901", WatchlistCategory.SANCTIONS, SubjectKind.INDIVIDUAL,
            new[] { "ZEPHANIAH OKOTH MBWANA" }, Array.Empty<BirthYearRange>(), Array.Empty<string>(), null);
        var candidate = new ScreeningCandidate(Guid.NewGuid(), "Zephaniah Mbwana", SubjectKind.INDIVIDUAL, null);
        return ScreeningMatch.Create(new TenantId(Guid.NewGuid()), candidate,
            new PotentialMatch(subject, "ZEPHANIAH OKOTH MBWANA", 0.9333, false), ScreeningTriggers.CustomerCreated);
    }

    [Fact]
    public void New_match_is_pending_and_copies_the_subject()
    {
        var match = NewMatch();
        Assert.Equal(ScreeningMatchStatus.PENDING_REVIEW, match.Status);
        Assert.Equal("QDi.901", match.ExternalId);
        Assert.Equal(WatchlistCategory.SANCTIONS, match.Category);
        Assert.Equal("Zephaniah Mbwana", match.CustomerName);
        Assert.Equal("ZEPHANIAH OKOTH MBWANA", match.Subject.Names[0]);
    }

    [Fact]
    public void Confirm_records_reviewer_reason_and_alert()
    {
        var match = NewMatch();
        var alertId = Guid.NewGuid();
        match.Confirm("user-1", "Same passport number", alertId);
        Assert.Equal(ScreeningMatchStatus.CONFIRMED, match.Status);
        Assert.Equal("user-1", match.ReviewedBy);
        Assert.Equal("Same passport number", match.ReviewReason);
        Assert.Equal(alertId, match.AlertId);
        Assert.NotNull(match.ReviewedAt);
    }

    [Fact]
    public void Review_requires_a_reason()
        => Assert.Throws<ArgumentException>(() => NewMatch().MarkFalsePositive("user-1", " "));

    [Fact]
    public void A_match_is_reviewed_once()
    {
        var match = NewMatch();
        match.MarkFalsePositive("user-1", "Different date of birth");
        Assert.Throws<ScreeningMatchAlreadyReviewedException>(() => match.Confirm("user-2", "Changed my mind", Guid.NewGuid()));
    }
}
