namespace Aegis.Shared.Domain;

using System.Collections.Generic;

/// <summary>
/// Base class for aggregate roots.
/// </summary>
public abstract class AggregateRoot : EntityBase, IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
