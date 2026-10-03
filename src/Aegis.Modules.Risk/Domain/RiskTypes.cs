namespace Aegis.Modules.Risk.Domain;

using Aegis.Shared.Domain;

public enum RiskFactorType
{
    GEOGRAPHY,
    CUSTOMER_TYPE,
    OPEN_ALERTS,
    HIGH_SEVERITY_ALERTS,
    SUSPICIOUS_CASES,
    TRANSACTION_ACTIVITY
}

public enum RiskBand
{
    LOW,
    MEDIUM,
    HIGH,
    CRITICAL
}

/// <summary>
/// One weighted factor. <see cref="Weight"/> is the most points the factor can add; the other
/// properties are parameters used only by the factor types that need them (see <see cref="RiskModelValidator"/>).
/// </summary>
public sealed record RiskFactorDefinition(
    RiskFactorType Type,
    int Weight,
    IReadOnlyList<string>? Countries = null,
    IReadOnlyList<string>? CustomerTypes = null,
    int? PointsEach = null,
    int? Threshold = null,
    int? WindowDays = null);

/// <summary>Lower bounds (inclusive) of the MEDIUM, HIGH and CRITICAL bands; below Medium is LOW.</summary>
public sealed record RiskBands(int Medium, int High, int Critical)
{
    public RiskBand BandOf(int score) =>
        score >= Critical ? RiskBand.CRITICAL
        : score >= High ? RiskBand.HIGH
        : score >= Medium ? RiskBand.MEDIUM
        : RiskBand.LOW;
}

public sealed record RiskAlertFact(AlertSeverity Severity, bool IsOpen, DateTimeOffset TriggeredAt);

/// <param name="TransactionCounts">Transaction count keyed by window length in days.</param>
public sealed record RiskInputs(
    string CustomerType,
    string Country,
    IReadOnlyList<RiskAlertFact> Alerts,
    int SuspiciousCaseCount,
    IReadOnlyDictionary<int, int> TransactionCounts,
    DateTimeOffset AsOf);

public sealed record RiskContribution(RiskFactorType Type, int Points, int Weight, string Detail);

public sealed record RiskAssessment(int Score, RiskBand Band, IReadOnlyList<RiskContribution> Contributions);
