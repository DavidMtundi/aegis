namespace Aegis.Shared.Domain;

using System;

/// <summary>
/// Represents a domain event.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
