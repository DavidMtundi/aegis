namespace Aegis.Modules.Alerts.Domain;

/// <summary>
/// Represents the status of an alert.
/// </summary>
public enum AlertStatus
{
    OPEN,
    ASSIGNED,
    IN_REVIEW,
    ESCALATED,
    RESOLVED,
    DISMISSED,
    CLOSED
}
