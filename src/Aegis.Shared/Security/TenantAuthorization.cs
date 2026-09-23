namespace Aegis.Shared.Security;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Analyst = "Analyst";
}

public static class TenantAuthorization
{
    public static bool IsAdmin(ITenantContext tenant)
        => tenant.IsAuthenticated
           && tenant.Roles.Any(r => string.Equals(r, RoleNames.Admin, StringComparison.OrdinalIgnoreCase));

    public static bool CanManageRules(ITenantContext tenant) => IsAdmin(tenant);

    public static bool CanCloseCases(ITenantContext tenant)
        => tenant.IsAuthenticated
           && (IsAdmin(tenant)
               || tenant.Roles.Any(r => string.Equals(r, RoleNames.Analyst, StringComparison.OrdinalIgnoreCase)));
}
