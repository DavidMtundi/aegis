namespace Aegis.Tests.Unit.Risk;

using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;

public sealed class RiskCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static RiskInputs Inputs(
        string country = "KE",
        string type = "INDIVIDUAL",
        IReadOnlyList<RiskAlertFact>? alerts = null,
        int suspiciousCases = 0,
        IReadOnlyDictionary<int, int>? txCounts = null) =>
        new(type, country, alerts ?? Array.Empty<RiskAlertFact>(), suspiciousCases,
            txCounts ?? new Dictionary<int, int>(), Now);

    [Fact]
    public void Clean_customer_scores_zero_and_low()
    {
        var result = RiskCalculator.Calculate(RiskModelDefaults.Factors, RiskModelDefaults.Bands, Inputs());

        Assert.Equal(0, result.Score);
        Assert.Equal(RiskBand.LOW, result.Band);
        Assert.Equal(RiskModelDefaults.Factors.Count, result.Contributions.Count);
        Assert.All(result.Contributions, c => Assert.Equal(0, c.Points));
    }

    [Fact]
    public void Geography_and_customer_type_add_their_full_weight()
    {
        var factors = new[]
        {
            new RiskFactorDefinition(RiskFactorType.GEOGRAPHY, 30, Countries: new[] { "IR" }),
            new RiskFactorDefinition(RiskFactorType.CUSTOMER_TYPE, 10, CustomerTypes: new[] { "BUSINESS" }),
        };

        var result = RiskCalculator.Calculate(factors, RiskModelDefaults.Bands, Inputs(country: "ir", type: "BUSINESS"));

        Assert.Equal(40, result.Score);
        Assert.Equal(RiskBand.MEDIUM, result.Band);
    }

    [Fact]
    public void Alert_and_case_factors_count_per_item_and_cap_at_weight()
    {
        var factors = new[]
        {
            new RiskFactorDefinition(RiskFactorType.OPEN_ALERTS, 20, PointsEach: 5),
            new RiskFactorDefinition(RiskFactorType.HIGH_SEVERITY_ALERTS, 20, PointsEach: 10, WindowDays: 90),
            new RiskFactorDefinition(RiskFactorType.SUSPICIOUS_CASES, 30, PointsEach: 15),
        };
        var alerts = new[]
        {
            new RiskAlertFact(AlertSeverity.HIGH, IsOpen: true, Now.AddDays(-1)),
            new RiskAlertFact(AlertSeverity.CRITICAL, IsOpen: false, Now.AddDays(-10)),
            new RiskAlertFact(AlertSeverity.HIGH, IsOpen: true, Now.AddDays(-200)),
            new RiskAlertFact(AlertSeverity.LOW, IsOpen: true, Now),
            new RiskAlertFact(AlertSeverity.MEDIUM, IsOpen: true, Now),
            new RiskAlertFact(AlertSeverity.MEDIUM, IsOpen: true, Now),
        };

        var result = RiskCalculator.Calculate(factors, RiskModelDefaults.Bands, Inputs(alerts: alerts, suspiciousCases: 3));

        // 5 open alerts x 5 = 25 -> capped 20; 2 high/critical within 90 days x 10 = 20; 3 cases x 15 = 45 -> capped 30.
        Assert.Equal(new[] { 20, 20, 30 }, result.Contributions.Select(c => c.Points));
        Assert.Equal(70, result.Score);
        Assert.Equal(RiskBand.HIGH, result.Band);
    }

    [Fact]
    public void Transaction_activity_scales_linearly_to_threshold()
    {
        var factors = new[] { new RiskFactorDefinition(RiskFactorType.TRANSACTION_ACTIVITY, 10, Threshold: 100, WindowDays: 30) };

        Assert.Equal(5, RiskCalculator.Calculate(factors, RiskModelDefaults.Bands,
            Inputs(txCounts: new Dictionary<int, int> { [30] = 50 })).Score);
        Assert.Equal(10, RiskCalculator.Calculate(factors, RiskModelDefaults.Bands,
            Inputs(txCounts: new Dictionary<int, int> { [30] = 400 })).Score);
        Assert.Equal(0, RiskCalculator.Calculate(factors, RiskModelDefaults.Bands, Inputs()).Score);
    }

    [Fact]
    public void Total_is_capped_at_100_and_critical_band_applies()
    {
        var factors = new[]
        {
            new RiskFactorDefinition(RiskFactorType.GEOGRAPHY, 80, Countries: new[] { "KP" }),
            new RiskFactorDefinition(RiskFactorType.CUSTOMER_TYPE, 80, CustomerTypes: new[] { "INDIVIDUAL" }),
        };

        var result = RiskCalculator.Calculate(factors, RiskModelDefaults.Bands, Inputs(country: "KP"));

        Assert.Equal(100, result.Score);
        Assert.Equal(RiskBand.CRITICAL, result.Band);
    }

    [Fact]
    public void Contributions_explain_why_points_were_added()
    {
        var factors = new[] { new RiskFactorDefinition(RiskFactorType.GEOGRAPHY, 30, Countries: new[] { "IR" }) };

        var contribution = RiskCalculator.Calculate(factors, RiskModelDefaults.Bands, Inputs(country: "IR")).Contributions.Single();

        Assert.Equal(RiskFactorType.GEOGRAPHY, contribution.Type);
        Assert.Equal(30, contribution.Weight);
        Assert.Contains("IR", contribution.Detail);
    }
}
