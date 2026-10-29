using System;
using Aegis.Modules.Aml.Domain;
using Aegis.Shared.Domain;
using Xunit;

namespace Aegis.Tests.Unit.Aml;

public class AmlRuleVersionLifecycleTests
{
    [Fact]
    public void Submit_approve_activate_happy_path()
    {
        var version = CreateDraft("creator");
        version.SubmitForApproval();
        Assert.Equal(RuleVersionStatus.PENDING_APPROVAL, version.Status);

        version.Approve("approver");
        Assert.Equal(RuleVersionStatus.APPROVED, version.Status);
        Assert.Equal("approver", version.ApprovedBy);

        version.Activate(DateTimeOffset.UtcNow);
        Assert.Equal(RuleVersionStatus.ACTIVE, version.Status);
    }

    [Fact]
    public void Approve_own_draft_throws()
    {
        var version = CreateDraft("same-user");
        version.SubmitForApproval();
        Assert.Throws<InvalidOperationException>(() => version.Approve("same-user"));
    }

    [Fact]
    public void Activate_from_draft_throws()
    {
        var version = CreateDraft("creator");
        Assert.Throws<InvalidOperationException>(() => version.Activate(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Suspend_from_active_then_resume()
    {
        var version = CreateActive();
        version.Suspend();
        Assert.Equal(RuleVersionStatus.SUSPENDED, version.Status);

        version.Resume();
        Assert.Equal(RuleVersionStatus.ACTIVE, version.Status);
    }

    [Fact]
    public void Retire_from_active()
    {
        var version = CreateActive();
        var at = DateTimeOffset.UtcNow;
        version.Retire(at);
        Assert.Equal(RuleVersionStatus.RETIRED, version.Status);
        Assert.Equal(at, version.EffectiveTo);
    }

    [Fact]
    public void Retire_from_suspended()
    {
        var version = CreateActive();
        version.Suspend();
        version.Retire(DateTimeOffset.UtcNow);
        Assert.Equal(RuleVersionStatus.RETIRED, version.Status);
    }

    [Fact]
    public void Suspend_from_draft_throws()
    {
        var version = CreateDraft("creator");
        Assert.Throws<InvalidOperationException>(() => version.Suspend());
    }

    [Fact]
    public void Resume_from_retired_throws()
    {
        var version = CreateActive();
        version.Retire(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => version.Resume());
    }

    [Fact]
    public void ReturnToDraft_from_pending()
    {
        var version = CreateDraft("creator");
        version.SubmitForApproval();
        version.ReturnToDraft();
        Assert.Equal(RuleVersionStatus.DRAFT, version.Status);
    }

    [Fact]
    public void UpdateDefinition_only_while_draft()
    {
        var version = CreateDraft("creator");
        version.UpdateDefinition(SampleDefinition() with { Name = "Renamed", RiskScore = 55 });
        Assert.Equal("Renamed", version.Definition.Name);
        Assert.Equal(55, version.Definition.RiskScore);

        version.SubmitForApproval();
        Assert.Throws<InvalidOperationException>(() =>
            version.UpdateDefinition(SampleDefinition() with { Name = "Nope" }));
    }

    private static AmlRuleVersion CreateDraft(string createdBy) =>
        AmlRuleVersion.Create(
            Guid.NewGuid(),
            new TenantId(Guid.NewGuid()),
            1,
            SampleDefinition(),
            createdBy);

    private static AmlRuleVersion CreateActive()
    {
        var version = CreateDraft("creator");
        version.SubmitForApproval();
        version.Approve("approver");
        version.Activate(DateTimeOffset.UtcNow);
        return version;
    }

    private static RuleDefinition SampleDefinition() => new()
    {
        Code = "TEST_001",
        Name = "Test",
        Schedule = new RuleSchedule { Frequency = "realtime", Lookback = "24h" },
        Conditions = new RuleConditionGroup
        {
            All = [new RuleCondition { Field = "amount", Operator = ">=", Value = 1 }]
        },
        Severity = AlertSeverity.MEDIUM,
        RiskScore = 40,
        Actions = ["CREATE_ALERT"]
    };
}
