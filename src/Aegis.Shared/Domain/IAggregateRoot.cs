namespace Aegis.Shared.Domain;

using System.Collections.Generic;

/// <summary>
/// Marker interface for aggregate roots.
/// </summary>
public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
}
