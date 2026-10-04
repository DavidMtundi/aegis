namespace Aegis.Application.Screening;

using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;

public sealed record ScreeningOptions(double MatchThreshold = WatchlistIndex.DefaultThreshold);

public static class ScreeningAlertPolicy
{
    /// <summary>PEP status calls for enhanced due diligence, not a freeze, so it is HIGH rather than CRITICAL.</summary>
    public static (AlertSeverity Severity, int RiskScore, string Title) For(WatchlistCategory category) => category switch
    {
        WatchlistCategory.SANCTIONS => (AlertSeverity.CRITICAL, 95, "Confirmed sanctions match"),
        WatchlistCategory.WATCHLIST => (AlertSeverity.CRITICAL, 90, "Confirmed watchlist match"),
        WatchlistCategory.PEP => (AlertSeverity.HIGH, 70, "Confirmed PEP match"),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };

    public static AlertEvidence Evidence(ScreeningMatch match, string title) => new()
    {
        RuleName = title,
        RuleVersionNumber = 1,
        EvaluatedValues = new Dictionary<string, object>
        {
            ["list"] = match.Source.ToString(),
            ["category"] = match.Category.ToString(),
            ["matchedName"] = match.MatchedName,
            ["customerName"] = match.CustomerName,
            ["matchScore"] = match.Score,
            ["externalId"] = match.ExternalId,
            ["birthYearMatched"] = match.BirthYearMatched
        },
        AdditionalContext = new Dictionary<string, object>
        {
            ["ruleCode"] = ScreeningAlertSource.RuleCode,
            ["screeningMatchId"] = match.Id.ToString("D"),
            ["reviewReason"] = match.ReviewReason ?? ""
        }
    };
}
