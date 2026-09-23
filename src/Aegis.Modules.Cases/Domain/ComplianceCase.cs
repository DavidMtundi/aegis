namespace Aegis.Modules.Cases.Domain;

using Aegis.Shared.Domain;

public sealed class ComplianceCase : AggregateRoot
{
    public Guid? CustomerId { get; private set; }
    public string Title { get; private set; } = null!;
    public CaseStatus Status { get; private set; }
    public CasePriority Priority { get; private set; }
    public string? AssignedTo { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public CaseDisposition? Disposition { get; private set; }
    public string? Conclusion { get; private set; }
    public List<Guid> LinkedAlertIds { get; private set; } = new();
    public List<CaseNote> Notes { get; private set; } = new();

    private ComplianceCase() { }

    public static ComplianceCase CreateFromAlert(
        TenantId tenantId,
        Guid alertId,
        string title,
        CasePriority priority,
        Guid? customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new ComplianceCase
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            Title = title.Trim(),
            Status = CaseStatus.OPEN,
            Priority = priority,
            OpenedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LinkedAlertIds = new List<Guid> { alertId },
            Notes = new List<CaseNote>()
        };
    }

    public void Assign(string assignedTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignedTo);
        if (Status == CaseStatus.CLOSED)
            throw new InvalidOperationException("Cannot assign a closed case.");
        AssignedTo = assignedTo.Trim();
        if (Status == CaseStatus.OPEN)
            Status = CaseStatus.INVESTIGATING;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddNote(string text, string authorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorId);
        if (Status == CaseStatus.CLOSED)
            throw new InvalidOperationException("Cannot add notes to a closed case.");
        Notes.Add(new CaseNote(Guid.NewGuid(), text.Trim(), authorId.Trim(), DateTimeOffset.UtcNow));
        if (Status == CaseStatus.OPEN)
            Status = CaseStatus.INVESTIGATING;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Close(CaseDisposition disposition, string conclusion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conclusion);
        if (Status == CaseStatus.CLOSED)
            throw new InvalidOperationException("Case is already closed.");
        Disposition = disposition;
        Conclusion = conclusion.Trim();
        Status = CaseStatus.CLOSED;
        ClosedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
