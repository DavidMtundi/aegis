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
        return new Tenant
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId.New(), // A tenant owns itself basically
            Name = name,
            Slug = slug,
            Status = TenantStatus.ONBOARDING,
            Plan = plan,
            ContactEmail = contactEmail,
            Settings = settings,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
