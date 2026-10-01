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

    public void SetRoles(IEnumerable<string> roleNames)
    {
        RoleNames = ValidateRoles(roleNames).ToList();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        Status = UserStatus.DISABLED;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Maps role names to their canonical built-in spelling; throws on unknown roles.</summary>
    public static IReadOnlyList<string> ValidateRoles(IEnumerable<string> roleNames)
    {
        var result = new List<string>();
        foreach (var raw in roleNames)
        {
            var name = raw.Trim();
            var canonical = Aegis.Shared.Security.RoleNames.All
                .FirstOrDefault(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown role '{name}'.", nameof(roleNames));
            if (!result.Contains(canonical)) result.Add(canonical);
        }
        return result;
    }
}
