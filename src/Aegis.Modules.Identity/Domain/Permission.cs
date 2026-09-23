namespace Aegis.Modules.Identity.Domain;

using System;

/// <summary>
/// Global permission catalog entry (not tenant-owned).
/// </summary>
public sealed class Permission
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    private Permission() { }

    public static Permission Create(string code, string description)
    {
        return new Permission
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToLowerInvariant(),
            Description = description
        };
    }
}
