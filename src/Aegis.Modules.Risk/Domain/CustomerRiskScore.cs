namespace Aegis.Modules.Risk.Domain;

using Aegis.Shared.Domain;

public static class RiskTriggers
{
    public const string CustomerCreated = "CUSTOMER_CREATED";
    public const string AlertCreated = "ALERT_CREATED";
    public const string Manual = "MANUAL";
    public const string Batch = "BATCH";
    public const string ModelChanged = "MODEL_CHANGED";
}

/// <summary>One calculated score; rows are append-only so the latest row is the current score and the rest is history.</summary>
public sealed class CustomerRiskScore : EntityBase
{
    public Guid CustomerId { get; private set; }
    public Guid ModelId { get; private set; }
    public int ModelVersion { get; private set; }
    public int Score { get; private set; }
    public RiskBand Band { get; private set; }
    public List<RiskContribution> Contributions { get; private set; } = new();
    public string Trigger { get; private set; } = null!;
    public string CalculatedBy { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    private CustomerRiskScore() { }

    public static CustomerRiskScore Record(
        TenantId tenantId, Guid customerId, RiskModel model, RiskAssessment assessment, string trigger, string calculatedBy)
    {
        var now = DateTimeOffset.UtcNow;
        return new CustomerRiskScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            ModelId = model.Id,
            ModelVersion = model.Version,
            Score = assessment.Score,
            Band = assessment.Band,
            Contributions = assessment.Contributions.ToList(),
            Trigger = trigger,
            CalculatedBy = calculatedBy,
            CalculatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
