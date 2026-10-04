namespace Aegis.Modules.Screening.Domain;

using Aegis.Shared.Domain;

/// <summary>
/// Which version of the lists a tenant's customers were last fully screened against. A tenant whose
/// <see cref="ListVersion"/> differs from the current one is due a rescreen.
/// </summary>
public sealed class TenantScreeningState : ITenantOwned
{
    public const int ListVersionMaxLength = 200;

    public TenantId TenantId { get; private set; }
    public string ListVersion { get; private set; } = null!;
    public DateTimeOffset ScreenedAt { get; private set; }

    private TenantScreeningState() { }

    public static TenantScreeningState Create(TenantId tenantId, string listVersion)
        => new() { TenantId = tenantId, ListVersion = listVersion, ScreenedAt = DateTimeOffset.UtcNow };

    public void Advance(string listVersion)
    {
        ListVersion = listVersion;
        ScreenedAt = DateTimeOffset.UtcNow;
    }
}
