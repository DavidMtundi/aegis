namespace Aegis.Tests.Unit.Alerts;

using Aegis.Modules.Alerts.Domain;
using Aegis.Shared.Domain;

public sealed class ScreeningAlertTests
{
    [Fact]
    public void Screening_alert_uses_the_fixed_rule_and_a_per_match_dedupe_key()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var customerId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var alert = Alert.CreateFromScreening(tenant, customerId, matchId, AlertSeverity.CRITICAL, 95, new AlertEvidence { RuleName = "Confirmed sanctions match" });

        Assert.Equal(ScreeningAlertSource.RuleId, alert.RuleId);
        Assert.Equal(ScreeningAlertSource.RuleId, alert.RuleVersionId);
        Assert.Equal(FocusType.CUSTOMER, alert.FocusType);
        Assert.Equal(customerId.ToString(), alert.FocusEntityId);
        Assert.Equal(AlertSeverity.CRITICAL, alert.Severity);
        Assert.Equal($"{tenant.Value:D}:screening:{matchId:D}", alert.DeduplicationKey);
    }
}
