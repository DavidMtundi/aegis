namespace Aegis.Modules.Risk.Domain;

using Aegis.Shared.Domain;

public static class RiskCalculator
{
    public const int MaxScore = 100;

    public static RiskAssessment Calculate(IReadOnlyList<RiskFactorDefinition> factors, RiskBands bands, RiskInputs inputs)
    {
        var contributions = factors.Select(f => Evaluate(f, inputs)).ToList();
        var score = Math.Clamp(contributions.Sum(c => c.Points), 0, MaxScore);
        return new RiskAssessment(score, bands.BandOf(score), contributions);
    }

    private static RiskContribution Evaluate(RiskFactorDefinition f, RiskInputs inputs) => f.Type switch
    {
        RiskFactorType.GEOGRAPHY => Matches(f, f.Countries, inputs.Country, $"country {inputs.Country.ToUpperInvariant()}"),
        RiskFactorType.CUSTOMER_TYPE => Matches(f, f.CustomerTypes, inputs.CustomerType, $"customer type {inputs.CustomerType}"),
        RiskFactorType.OPEN_ALERTS => PerItem(f, inputs.Alerts.Count(a => a.IsOpen), "open alert(s)"),
        RiskFactorType.HIGH_SEVERITY_ALERTS => PerItem(
            f,
            inputs.Alerts.Count(a =>
                a.Severity is AlertSeverity.HIGH or AlertSeverity.CRITICAL
                && a.TriggeredAt >= inputs.AsOf.AddDays(-(f.WindowDays ?? 0))),
            $"high/critical alert(s) in {f.WindowDays} days"),
        RiskFactorType.SUSPICIOUS_CASES => PerItem(f, inputs.SuspiciousCaseCount, "case(s) closed as suspicious or reported"),
        RiskFactorType.TRANSACTION_ACTIVITY => Activity(f, inputs),
        _ => new RiskContribution(f.Type, 0, f.Weight, "unsupported factor")
    };

    private static RiskContribution Matches(RiskFactorDefinition f, IReadOnlyList<string>? list, string value, string label)
    {
        var hit = list?.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) == true;
        return new RiskContribution(f.Type, hit ? f.Weight : 0, f.Weight, hit ? $"{label} is listed" : $"{label} is not listed");
    }

    private static RiskContribution PerItem(RiskFactorDefinition f, int count, string label)
    {
        var points = Math.Min(f.Weight, count * (f.PointsEach ?? 0));
        return new RiskContribution(f.Type, points, f.Weight, $"{count} {label}");
    }

    private static RiskContribution Activity(RiskFactorDefinition f, RiskInputs inputs)
    {
        var window = f.WindowDays ?? 0;
        var count = inputs.TransactionCounts.TryGetValue(window, out var c) ? c : 0;
        var threshold = Math.Max(1, f.Threshold ?? 1);
        var points = (int)Math.Round(f.Weight * Math.Min(1.0, (double)count / threshold), MidpointRounding.AwayFromZero);
        return new RiskContribution(f.Type, points, f.Weight, $"{count} transaction(s) in {window} days (full weight at {threshold})");
    }
}
