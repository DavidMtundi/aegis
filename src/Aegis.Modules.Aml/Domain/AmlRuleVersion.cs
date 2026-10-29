namespace Aegis.Modules.Aml.Domain;

using System;
using System.Text.Json;
using Aegis.Shared.Domain;

/// <summary>
/// An immutable, versioned snapshot of an AML rule definition.
/// Once APPROVED or ACTIVE, a rule version must not be modified.
/// Any policy change requires creating a new version.
/// </summary>
public sealed class AmlRuleVersion : EntityBase
{
    public Guid RuleId { get; private set; }
    public int VersionNumber { get; private set; }
    public RuleDefinition Definition { get; private set; } = null!;
    public RuleVersionStatus Status { get; private set; }
    public DateTimeOffset? EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? ApprovedBy { get; private set; }

    // ── Convenience properties used by the evaluation engine ─────────────────

    /// <summary>Short code identifier from the rule definition.</summary>
    public string RuleCode => Definition?.Code ?? string.Empty;

    /// <summary>Human-readable name from the rule definition.</summary>
    public string RuleName => Definition?.Name ?? string.Empty;

    /// <summary>
    /// JSON-serialized definition. Used by the evaluation engine and tests.
    /// Setting this init-property deserializes the JSON into <see cref="Definition"/>.
    /// </summary>
    public string DefinitionJson
    {
        get => JsonSerializer.Serialize(Definition, _jsonOptions);
        init => Definition = JsonSerializer.Deserialize<RuleDefinition>(value, _jsonOptions)
                             ?? new RuleDefinition();
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Creates an ACTIVE rule version for persisted seeding (same model as compliance-authored rules).</summary>
    public static AmlRuleVersion CreateActive(
        Guid ruleId,
        TenantId tenantId,
        int versionNumber,
        RuleDefinition definition,
        string createdBy,
        DateTimeOffset effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        return new AmlRuleVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RuleId = ruleId,
            VersionNumber = versionNumber,
            Definition = definition,
            Status = RuleVersionStatus.ACTIVE,
            EffectiveFrom = effectiveFrom,
            CreatedBy = createdBy,
            ApprovedAt = effectiveFrom,
            ApprovedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private AmlRuleVersion() { }

    /// <summary>Creates a new DRAFT rule version.</summary>
    public static AmlRuleVersion Create(
        Guid ruleId,
        TenantId tenantId,
        int versionNumber,
        RuleDefinition definition,
        string createdBy)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        return new AmlRuleVersion
        {
            Id            = Guid.NewGuid(),
            TenantId      = tenantId,
            RuleId        = ruleId,
            VersionNumber = versionNumber,
            Definition    = definition,
            Status        = RuleVersionStatus.DRAFT,
            CreatedBy     = createdBy,
            CreatedAt     = DateTimeOffset.UtcNow,
            UpdatedAt     = DateTimeOffset.UtcNow
        };
    }

    public void UpdateDefinition(RuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Status != RuleVersionStatus.DRAFT)
            throw new InvalidOperationException($"Cannot edit a rule version in status {Status}.");

        Definition = definition;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SubmitForApproval()
    {
        if (Status != RuleVersionStatus.DRAFT)
            throw new InvalidOperationException($"Cannot submit a rule version in status {Status}.");

        Status = RuleVersionStatus.PENDING_APPROVAL;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(string approvedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        if (Status != RuleVersionStatus.PENDING_APPROVAL)
            throw new InvalidOperationException($"Cannot approve a rule version in status {Status}.");
        if (string.Equals(approvedBy, CreatedBy, StringComparison.Ordinal))
            throw new InvalidOperationException("Creator cannot approve their own rule version.");

        Status     = RuleVersionStatus.APPROVED;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovedBy = approvedBy;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void ReturnToDraft()
    {
        if (Status != RuleVersionStatus.PENDING_APPROVAL)
            throw new InvalidOperationException($"Cannot return a rule version in status {Status} to draft.");

        Status = RuleVersionStatus.DRAFT;
        ApprovedAt = null;
        ApprovedBy = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate(DateTimeOffset effectiveFrom)
    {
        if (Status != RuleVersionStatus.APPROVED)
            throw new InvalidOperationException($"Cannot activate a rule version in status {Status}.");

        Status        = RuleVersionStatus.ACTIVE;
        EffectiveFrom = effectiveFrom;
        UpdatedAt     = DateTimeOffset.UtcNow;
    }

    public void Supersede(DateTimeOffset effectiveTo)
    {
        Status      = RuleVersionStatus.SUPERSEDED;
        EffectiveTo = effectiveTo;
        UpdatedAt   = DateTimeOffset.UtcNow;
    }

    public void Suspend()
    {
        if (Status != RuleVersionStatus.ACTIVE)
            throw new InvalidOperationException($"Cannot suspend a rule version in status {Status}.");

        Status = RuleVersionStatus.SUSPENDED;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Resume()
    {
        if (Status != RuleVersionStatus.SUSPENDED)
            throw new InvalidOperationException($"Cannot resume a rule version in status {Status}.");

        Status = RuleVersionStatus.ACTIVE;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Retire(DateTimeOffset effectiveTo)
    {
        if (Status is not (RuleVersionStatus.ACTIVE or RuleVersionStatus.SUSPENDED))
            throw new InvalidOperationException($"Cannot retire a rule version in status {Status}.");

        Status = RuleVersionStatus.RETIRED;
        EffectiveTo ??= effectiveTo;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
