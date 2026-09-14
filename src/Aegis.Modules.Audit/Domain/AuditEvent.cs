namespace Aegis.Modules.Audit.Domain;

using System;

public sealed class AuditEvent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string? ActorRole { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? BeforeState { get; private set; }
    public string? AfterState { get; private set; }
    public string? Reason { get; private set; }
    public string? CorrelationId { get; private set; }

    private AuditEvent() { }

    public static AuditEvent Create(
        Guid tenantId,
        string eventType,
        string entityType,
        string entityId,
        string actorId,
        string? actorRole,
        string? beforeState,
        string? afterState,
        string? reason,
        string? correlationId)
    {
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            ActorId = actorId,
            ActorRole = actorRole,
            OccurredAt = DateTimeOffset.UtcNow,
            BeforeState = beforeState,
            AfterState = afterState,
            Reason = reason,
            CorrelationId = correlationId
        };
    }
}
