namespace Aegis.Modules.Alerts.Domain;

using System;
using Aegis.Shared.Domain;
// FocusType and AlertSeverity are defined in Aegis.Shared.Domain

public sealed class Alert : AggregateRoot
{
    public Guid RuleId { get; private set; }
    public Guid RuleVersionId { get; private set; }
    public FocusType FocusType { get; private set; }
    public string FocusEntityId { get; private set; } = null!;
    public AlertSeverity Severity { get; private set; }
    public int RiskScore { get; private set; }
    public DateTimeOffset TriggeredAt { get; private set; }
    public AlertStatus Status { get; private set; }
    public AlertEvidence Evidence { get; private set; } = null!;
    public string DeduplicationKey { get; private set; } = null!;
    public string? AssignedTo { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? DismissalReason { get; private set; }

    private Alert() { }

    public static Alert Create(
        TenantId tenantId,
        Guid ruleId,
        Guid ruleVersionId,
        FocusType focusType,
        string focusEntityId,
        AlertSeverity severity,
        int riskScore,
        AlertEvidence evidence,
        string deduplicationKey)
    {
        return new Alert
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RuleId = ruleId,
            RuleVersionId = ruleVersionId,
            FocusType = focusType,
            FocusEntityId = focusEntityId,
            Severity = severity,
            RiskScore = riskScore,
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = AlertStatus.OPEN,
            Evidence = evidence,
            DeduplicationKey = deduplicationKey,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool IsClosed => Status is AlertStatus.RESOLVED or AlertStatus.DISMISSED or AlertStatus.CLOSED;

    public void Assign(string assignedTo)
    {
        EnsureOpen();
        AssignedTo = assignedTo;
        if (Status != AlertStatus.ESCALATED)
        {
            Status = AlertStatus.ASSIGNED;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Resolve(string resolvedBy)
    {
        EnsureOpen();
        Status = AlertStatus.RESOLVED;
        ResolvedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Dismiss(string dismissedBy, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A dismissal reason is required.", nameof(reason));
        }
        EnsureOpen();
        Status = AlertStatus.DISMISSED;
        DismissalReason = reason.Trim();
        ResolvedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Escalate(string escalatedBy)
    {
        EnsureOpen();
        if (Status == AlertStatus.ESCALATED)
        {
            throw new InvalidOperationException("Alert is already escalated.");
        }
        Status = AlertStatus.ESCALATED;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new InvalidOperationException($"Alert is {Status} and cannot be changed.");
        }
    }
}
