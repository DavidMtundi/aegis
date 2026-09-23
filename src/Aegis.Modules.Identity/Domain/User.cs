namespace Aegis.Modules.Identity.Domain;

using System;
using System.Collections.Generic;
using Aegis.Shared.Domain;

public sealed class User : AggregateRoot
{
    public string Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserStatus Status { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public List<string> RoleNames { get; private set; } = new();

    private User() { }

    public static User Create(TenantId tenantId, string email, string name, string passwordHash, IEnumerable<string> roleNames)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            PasswordHash = passwordHash,
            Status = UserStatus.ACTIVE,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RoleNames = roleNames.Select(r => r.Trim()).Where(r => r.Length > 0).ToList()
        };

        return user;
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
