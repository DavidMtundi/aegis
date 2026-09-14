namespace Aegis.Modules.Identity.Domain;

public sealed record TenantSettings
{
    public string DefaultCurrency { get; init; } = "USD";
    public string DefaultCountry { get; init; } = "US";
    public string Timezone { get; init; } = "UTC";
    public string Locale { get; init; } = "en-US";
}
