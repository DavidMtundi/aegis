namespace Aegis.Modules.Aml.Domain.Events;

using System;
using Aegis.Shared.Domain;

public sealed record RuleCreatedEvent(Guid RuleId, TenantId TenantId, string Code, string CreatedBy) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
