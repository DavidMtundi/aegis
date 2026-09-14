namespace Aegis.Modules.Aml.Domain.Events;

using System;
using Aegis.Shared.Domain;

public sealed record RuleApprovedEvent(Guid RuleId, TenantId TenantId, Guid RuleVersionId, string ApprovedBy) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
