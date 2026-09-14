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

    internal void Approve(string approvedBy)
    {
        Status     = RuleVersionStatus.APPROVED;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovedBy = approvedBy;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    internal void Activate(DateTimeOffset effectiveFrom)
    {
        Status        = RuleVersionStatus.ACTIVE;
        EffectiveFrom = effectiveFrom;
        UpdatedAt     = DateTimeOffset.UtcNow;
    }

    internal void Supersede(DateTimeOffset effectiveTo)
    {
        Status      = RuleVersionStatus.SUPERSEDED;
        EffectiveTo = effectiveTo;
        UpdatedAt   = DateTimeOffset.UtcNow;
    }
}
