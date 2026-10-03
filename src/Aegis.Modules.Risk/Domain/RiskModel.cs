namespace Aegis.Modules.Risk.Domain;

using Aegis.Shared.Domain;

/// <summary>A tenant's risk model. Versions are immutable; saving a change creates the next version.</summary>
public sealed class RiskModel : AggregateRoot
{
    public int Version { get; private set; }
    public bool IsActive { get; private set; }
    public List<RiskFactorDefinition> Factors { get; private set; } = new();
    public RiskBands Bands { get; private set; } = RiskModelDefaults.Bands;
    public string CreatedBy { get; private set; } = null!;

    private RiskModel() { }

    public static RiskModel CreateDefault(TenantId tenantId, string createdBy)
        => Create(tenantId, 1, RiskModelDefaults.Factors, RiskModelDefaults.Bands, createdBy);

    public static RiskModel CreateNextVersion(
        RiskModel current, IReadOnlyList<RiskFactorDefinition> factors, RiskBands bands, string createdBy)
    {
        var errors = RiskModelValidator.Validate(factors, bands);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        current.IsActive = false;
        current.UpdatedAt = DateTimeOffset.UtcNow;
        return Create(current.TenantId, current.Version + 1, factors, bands, createdBy);
    }

    public RiskAssessment Assess(RiskInputs inputs) => RiskCalculator.Calculate(Factors, Bands, inputs);

    private static RiskModel Create(
        TenantId tenantId, int version, IReadOnlyList<RiskFactorDefinition> factors, RiskBands bands, string createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);
        var now = DateTimeOffset.UtcNow;
        return new RiskModel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Version = version,
            IsActive = true,
            Factors = factors.Select(Normalize).ToList(),
            Bands = bands,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static RiskFactorDefinition Normalize(RiskFactorDefinition f) => f with
    {
        Countries = f.Countries?.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList(),
        CustomerTypes = f.CustomerTypes?.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList()
    };
}

public static class RiskModelDefaults
{
    public static readonly RiskBands Bands = new(25, 50, 75);

    public static readonly IReadOnlyList<RiskFactorDefinition> Factors = new[]
    {
        new RiskFactorDefinition(RiskFactorType.GEOGRAPHY, 30, Countries: new[] { "KP", "IR", "SY", "MM" }),
        new RiskFactorDefinition(RiskFactorType.CUSTOMER_TYPE, 10, CustomerTypes: new[] { "BUSINESS" }),
        new RiskFactorDefinition(RiskFactorType.OPEN_ALERTS, 20, PointsEach: 5),
        new RiskFactorDefinition(RiskFactorType.HIGH_SEVERITY_ALERTS, 20, PointsEach: 10, WindowDays: 90),
        new RiskFactorDefinition(RiskFactorType.SUSPICIOUS_CASES, 30, PointsEach: 15),
        new RiskFactorDefinition(RiskFactorType.TRANSACTION_ACTIVITY, 10, Threshold: 100, WindowDays: 30),
    };
}
