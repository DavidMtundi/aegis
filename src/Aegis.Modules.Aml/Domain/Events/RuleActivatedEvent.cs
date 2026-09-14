namespace Aegis.Modules.Aml.Domain.Events;

using System;
using Aegis.Shared.Domain;

public sealed record RuleActivatedEvent(Guid RuleId, TenantId TenantId, Guid RuleVersionId, string ActivatedBy, DateTimeOffset EffectiveFrom) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
