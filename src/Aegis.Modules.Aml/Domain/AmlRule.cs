namespace Aegis.Modules.Aml.Domain;

using System;
using System.Collections.Generic;
using System.Linq;
using Aegis.Shared.Domain;
using Aegis.Modules.Aml.Domain.Events;

public sealed class AmlRule : AggregateRoot
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public ScenarioType ScenarioType { get; private set; }
    public RuleStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    
    private readonly List<AmlRuleVersion> _versions = new();
    public IReadOnlyCollection<AmlRuleVersion> Versions => _versions.AsReadOnly();

    private AmlRule() { }

    public static AmlRule CreateDraft(TenantId tenantId, string code, string name, string description, ScenarioType scenarioType, string createdBy)
    {
        var rule = new AmlRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            Description = description,
            ScenarioType = scenarioType,
            Status = RuleStatus.DRAFT,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        
        rule.AddDomainEvent(new RuleCreatedEvent(rule.Id, tenantId, code, createdBy));
        return rule;
    }

    public AmlRuleVersion? GetActiveVersion() => _versions.FirstOrDefault(v => v.Status == RuleVersionStatus.ACTIVE);

    public void AddVersion(AmlRuleVersion version)
    {
        _versions.Add(version);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SubmitForApproval()
    {
        Status = RuleStatus.PENDING_APPROVAL;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve()
    {
        Status = RuleStatus.APPROVED;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        Status = RuleStatus.ACTIVE;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Disable()
    {
        Status = RuleStatus.DISABLED;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Retire()
    {
        Status = RuleStatus.RETIRED;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
