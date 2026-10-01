namespace Aegis.Tests.Unit.Cases;

using Aegis.Modules.Cases.Domain;
using Aegis.Shared.Domain;

public sealed class CaseWorkflowTests
{
    private static ComplianceCase NewCase(Guid? alertId = null) => ComplianceCase.CreateFromAlert(
        new TenantId(Guid.NewGuid()), alertId ?? Guid.NewGuid(), "Case", CasePriority.HIGH, null);

    [Fact]
    public void LinkAlert_adds_once_and_reports_duplicates()
    {
        var c = NewCase();
        var other = Guid.NewGuid();

        Assert.True(c.LinkAlert(other));
        Assert.False(c.LinkAlert(other));
        Assert.Equal(2, c.LinkedAlertIds.Count);
    }

    [Fact]
    public void Escalate_sets_status_and_rejects_repeat()
    {
        var c = NewCase();

        c.Escalate();

        Assert.Equal(CaseStatus.ESCALATED, c.Status);
        Assert.Throws<InvalidOperationException>(() => c.Escalate());
    }

    [Fact]
    public void Assigning_or_noting_an_escalated_case_keeps_it_escalated()
    {
        var c = NewCase();
        c.Escalate();

        c.Assign("reviewer");
        c.AddNote("Looking into it", "reviewer");

        Assert.Equal(CaseStatus.ESCALATED, c.Status);
    }

    [Fact]
    public void Closed_case_rejects_link_and_escalate()
    {
        var c = NewCase();
        c.Close(CaseDisposition.FALSE_POSITIVE, "Done");

        Assert.Throws<InvalidOperationException>(() => c.LinkAlert(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => c.Escalate());
    }
}
