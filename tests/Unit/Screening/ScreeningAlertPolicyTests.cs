namespace Aegis.Tests.Unit.Screening;

using Aegis.Application.Screening;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;

public sealed class ScreeningAlertPolicyTests
{
    [Theory]
    [InlineData(WatchlistCategory.SANCTIONS, AlertSeverity.CRITICAL, 95, "Confirmed sanctions match")]
    [InlineData(WatchlistCategory.WATCHLIST, AlertSeverity.CRITICAL, 90, "Confirmed watchlist match")]
    [InlineData(WatchlistCategory.PEP, AlertSeverity.HIGH, 70, "Confirmed PEP match")]
    public void Severity_follows_the_category(WatchlistCategory category, AlertSeverity severity, int risk, string title)
        => Assert.Equal((severity, risk, title), ScreeningAlertPolicy.For(category));

    [Fact]
    public void Evidence_carries_what_the_console_needs()
    {
        var subject = new WatchlistSubject(WatchlistSource.UN, "QDi.901", WatchlistCategory.SANCTIONS, SubjectKind.INDIVIDUAL,
            new[] { "ZEPHANIAH OKOTH MBWANA" }, Array.Empty<BirthYearRange>(), Array.Empty<string>(), null);
        var match = ScreeningMatch.Create(new TenantId(Guid.NewGuid()),
            new ScreeningCandidate(Guid.NewGuid(), "Zephaniah Mbwana", SubjectKind.INDIVIDUAL, null),
            new PotentialMatch(subject, "ZEPHANIAH OKOTH MBWANA", 0.9333, true), ScreeningTriggers.Manual);
        match.Confirm("user-1", "Same ID number", Guid.NewGuid());

        var evidence = ScreeningAlertPolicy.Evidence(match, "Confirmed sanctions match");

        Assert.Equal("Confirmed sanctions match", evidence.RuleName);
        Assert.Equal("UN", evidence.EvaluatedValues["list"]);
        Assert.Equal("SANCTIONS", evidence.EvaluatedValues["category"]);
        Assert.Equal("ZEPHANIAH OKOTH MBWANA", evidence.EvaluatedValues["matchedName"]);
        Assert.Equal(0.9333, evidence.EvaluatedValues["matchScore"]);
        Assert.Equal(true, evidence.EvaluatedValues["birthYearMatched"]);
        Assert.Equal(ScreeningAlertSource.RuleCode, evidence.AdditionalContext["ruleCode"]);
        Assert.Equal(match.Id.ToString("D"), evidence.AdditionalContext["screeningMatchId"]);
        Assert.Equal("Same ID number", evidence.AdditionalContext["reviewReason"]);
        Assert.Empty(evidence.TransactionIds);
    }
}
