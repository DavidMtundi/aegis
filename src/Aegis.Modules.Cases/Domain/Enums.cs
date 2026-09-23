namespace Aegis.Modules.Cases.Domain;

public enum CaseStatus
{
    OPEN,
    INVESTIGATING,
    PENDING_REVIEW,
    ESCALATED,
    CLOSED
}

public enum CasePriority
{
    LOW,
    MEDIUM,
    HIGH,
    CRITICAL
}

public enum CaseDisposition
{
    FALSE_POSITIVE,
    NO_SUSPICIOUS_ACTIVITY,
    SUSPICIOUS_ACTIVITY,
    ESCALATED,
    REPORTED,
    OTHER
}
