namespace Aegis.Application.Cases;

using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Cases.Domain;

public sealed record CaseTimelineEntry(
    DateTimeOffset At,
    string Kind,
    string EventType,
    string EntityType,
    string EntityId,
    string ActorId,
    string? Summary,
    string? Details);

/// <summary>
/// Case history from audit events on the case and its linked alerts, plus note text.
/// </summary>
public static class CaseTimeline
{
    public const string AuditKind = "AUDIT";
    public const string NoteKind = "NOTE";

    /// <summary>Entity ids whose audit events belong on the timeline.</summary>
    public static IReadOnlyCollection<string> EntityIds(ComplianceCase c)
        => c.LinkedAlertIds.Select(id => id.ToString()).Append(c.Id.ToString()).ToList();

    public static IReadOnlyList<CaseTimelineEntry> Build(ComplianceCase c, IEnumerable<AuditEvent> events)
    {
        var caseId = c.Id.ToString();
        var related = EntityIds(c).ToHashSet();

        var audit = events
            .Where(e => related.Contains(e.EntityId))
            .Where(e => !(e.EntityId == caseId && e.EventType == AuditEventTypes.CASE_NOTE_ADDED))
            .Select(e => new CaseTimelineEntry(
                e.OccurredAt, AuditKind, e.EventType, e.EntityType, e.EntityId, e.ActorId, e.Reason, e.AfterState));

        var notes = c.Notes.Select(n => new CaseTimelineEntry(
            n.CreatedAt, NoteKind, AuditEventTypes.CASE_NOTE_ADDED, nameof(ComplianceCase), caseId, n.AuthorId, n.Text, null));

        return audit.Concat(notes).OrderBy(e => e.At).ToList();
    }
}
