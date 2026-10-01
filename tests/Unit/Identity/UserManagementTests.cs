namespace Aegis.Tests.Unit.Identity;

using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Security;

public sealed class UserManagementTests
{
    private static User NewUser(params string[] roles)
        => User.Create(TenantId.New(), "a@b.test", "A", "hash", roles);

    [Fact]
    public void SetRoles_replaces_roles_and_normalizes_casing()
    {
        var user = NewUser(RoleNames.Viewer);

        user.SetRoles(new[] { "analyst", "Reviewer" });

        Assert.Equal(new[] { RoleNames.Analyst, RoleNames.Reviewer }, user.RoleNames);
    }

    [Fact]
    public void SetRoles_rejects_unknown_roles()
    {
        var user = NewUser(RoleNames.Viewer);

        Assert.Throws<ArgumentException>(() => user.SetRoles(new[] { "Superuser" }));
        Assert.Equal(new[] { RoleNames.Viewer }, user.RoleNames);
    }

    [Fact]
    public void Deactivate_disables_the_user()
    {
        var user = NewUser(RoleNames.Analyst);

        user.Deactivate();

        Assert.Equal(UserStatus.DISABLED, user.Status);
    }

    [Fact]
    public void ValidateRoles_returns_canonical_names()
    {
        Assert.Equal(new[] { RoleNames.Admin }, User.ValidateRoles(new[] { " admin ", "ADMIN" }));
    }
}
