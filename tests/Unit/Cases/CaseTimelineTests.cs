namespace Aegis.Tests.Unit.Cases;

using Aegis.Application.Cases;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Cases.Domain;
using Aegis.Shared.Domain;

public sealed class CaseTimelineTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static AuditEvent Event(string type, string entityType, string entityId, string? after = null, string? reason = null)
        => AuditEvent.Create(Tenant, type, entityType, entityId, "actor-1", "Analyst", null, after, reason, "trace");

    [Fact]
    public void Merges_case_and_alert_events_with_notes_in_time_order()
    {
        var alertId = Guid.NewGuid();
        var c = ComplianceCase.CreateFromAlert(new TenantId(Tenant), alertId, "Case", CasePriority.HIGH, null);
        var alertCreated = Event(AuditEventTypes.ALERT_CREATED, "Alert", alertId.ToString(), reason: "Rule fired");
        var caseCreated = Event(AuditEventTypes.CASE_CREATED, nameof(ComplianceCase), c.Id.ToString(), reason: "Case created from alert");
        c.AddNote("Called the customer", "actor-1");
        var noteAudit = Event(AuditEventTypes.CASE_NOTE_ADDED, nameof(ComplianceCase), c.Id.ToString());

        var timeline = CaseTimeline.Build(c, new[] { caseCreated, noteAudit, alertCreated });

        Assert.Equal(
            new[] { AuditEventTypes.ALERT_CREATED, AuditEventTypes.CASE_CREATED, AuditEventTypes.CASE_NOTE_ADDED },
            timeline.Select(e => e.EventType).ToArray());
        var note = timeline.Last();
        Assert.Equal(CaseTimeline.NoteKind, note.Kind);
        Assert.Equal("Called the customer", note.Summary);
        Assert.Equal("Rule fired", timeline.First().Summary);
    }

    [Fact]
    public void Ignores_events_for_unrelated_entities()
    {
        var c = ComplianceCase.CreateFromAlert(new TenantId(Tenant), Guid.NewGuid(), "Case", CasePriority.LOW, null);
        var unrelated = Event(AuditEventTypes.ALERT_CREATED, "Alert", Guid.NewGuid().ToString());

        var timeline = CaseTimeline.Build(c, new[] { unrelated });

        Assert.Empty(timeline);
    }
}
