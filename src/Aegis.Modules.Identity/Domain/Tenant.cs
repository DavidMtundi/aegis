namespace Aegis.Modules.Identity.Domain;

using System;
using Aegis.Shared.Domain;

public sealed class Tenant : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public TenantStatus Status { get; private set; }
    public string Plan { get; private set; } = null!;
    public string ContactEmail { get; private set; } = null!;
    public TenantSettings Settings { get; private set; } = new();

    private Tenant() { }

    public static Tenant Create(string name, string slug, string plan, string contactEmail, TenantSettings settings)
    {
        var id = Guid.NewGuid();
        return new Tenant
        {
            Id = id,
            TenantId = new TenantId(id),
            Name = name,
            Slug = slug.Trim().ToLowerInvariant(),
            Status = TenantStatus.ONBOARDING,
            Plan = plan,
            ContactEmail = contactEmail,
            Settings = settings,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Activate()
    {
        Status = TenantStatus.ACTIVE;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
