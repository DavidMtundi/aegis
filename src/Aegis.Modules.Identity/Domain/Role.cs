namespace Aegis.Modules.Identity.Domain;

using System;
using Aegis.Shared.Domain;

public sealed class Role : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    private Role() { }

    public static Role Create(TenantId tenantId, string name, string description)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
