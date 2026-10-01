namespace Aegis.Tests.Unit.Alerts;

using Aegis.Modules.Alerts.Domain;
using Aegis.Shared.Domain;

public sealed class AlertWorkflowTests
{
    private static Alert NewAlert() => Alert.Create(
        new TenantId(Guid.NewGuid()),
        Guid.NewGuid(),
        Guid.NewGuid(),
        FocusType.CUSTOMER,
        Guid.NewGuid().ToString(),
        AlertSeverity.HIGH,
        80,
        new AlertEvidence { RuleName = "Structuring" },
        "key");

    [Fact]
    public void Dismiss_requires_a_reason()
    {
        var alert = NewAlert();

        Assert.Throws<ArgumentException>(() => alert.Dismiss("analyst", "  "));
        Assert.Equal(AlertStatus.OPEN, alert.Status);
    }

    [Fact]
    public void Dismiss_records_trimmed_reason()
    {
        var alert = NewAlert();

        alert.Dismiss("analyst", "  Known salary pattern  ");

        Assert.Equal(AlertStatus.DISMISSED, alert.Status);
        Assert.Equal("Known salary pattern", alert.DismissalReason);
        Assert.NotNull(alert.ResolvedAt);
    }

    [Fact]
    public void Escalate_moves_open_alert_to_escalated()
    {
        var alert = NewAlert();

        alert.Escalate("analyst");

        Assert.Equal(AlertStatus.ESCALATED, alert.Status);
    }

    [Fact]
    public void Assigning_an_escalated_alert_keeps_it_escalated()
    {
        var alert = NewAlert();
        alert.Escalate("analyst");

        alert.Assign("reviewer-id");

        Assert.Equal(AlertStatus.ESCALATED, alert.Status);
        Assert.Equal("reviewer-id", alert.AssignedTo);
    }

    [Fact]
    public void Escalating_twice_is_rejected()
    {
        var alert = NewAlert();
        alert.Escalate("analyst");

        Assert.Throws<InvalidOperationException>(() => alert.Escalate("analyst"));
    }

    [Theory]
    [InlineData("dismiss")]
    [InlineData("resolve")]
    public void Closed_alerts_reject_further_work(string closeWith)
    {
        var alert = NewAlert();
        if (closeWith == "dismiss") alert.Dismiss("analyst", "false positive");
        else alert.Resolve("analyst");

        Assert.Throws<InvalidOperationException>(() => alert.Assign("someone"));
        Assert.Throws<InvalidOperationException>(() => alert.Escalate("analyst"));
        Assert.Throws<InvalidOperationException>(() => alert.Resolve("analyst"));
        Assert.Throws<InvalidOperationException>(() => alert.Dismiss("analyst", "again"));
    }
}
