namespace Aegis.Tests.Unit.Security;

using Aegis.Shared.Security;

public sealed class RolePermissionsTests
{
    [Fact]
    public void Admin_has_every_permission()
    {
        Assert.All(Permissions.All, p => Assert.True(RolePermissions.Grants(new[] { RoleNames.Admin }, p)));
    }

    [Theory]
    [InlineData(Permissions.AlertRead, true)]
    [InlineData(Permissions.CaseRead, true)]
    [InlineData(Permissions.AlertDismiss, false)]
    [InlineData(Permissions.CustomerWrite, false)]
    [InlineData(Permissions.AuditRead, false)]
    public void Viewer_is_read_only(string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Grants(new[] { RoleNames.Viewer }, permission));
    }

    [Theory]
    [InlineData(Permissions.AlertDismiss, true)]
    [InlineData(Permissions.CaseClose, true)]
    [InlineData(Permissions.TransactionWrite, true)]
    [InlineData(Permissions.AuditRead, false)]
    [InlineData(Permissions.RuleActivate, false)]
    [InlineData(Permissions.UserManage, false)]
    public void Analyst_works_alerts_and_cases_but_not_rules_or_audit(string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Grants(new[] { RoleNames.Analyst }, permission));
    }

    [Theory]
    [InlineData(Permissions.AuditRead, true)]
    [InlineData(Permissions.CaseClose, true)]
    [InlineData(Permissions.RuleActivate, false)]
    public void Reviewer_adds_audit_read_to_analyst(string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Grants(new[] { RoleNames.Reviewer }, permission));
    }

    [Fact]
    public void Role_names_are_case_insensitive_and_unknown_roles_grant_nothing()
    {
        Assert.True(RolePermissions.Grants(new[] { "analyst" }, Permissions.AlertAssign));
        Assert.False(RolePermissions.Grants(new[] { "Superuser" }, Permissions.AlertRead));
        Assert.False(RolePermissions.Grants(Array.Empty<string>(), Permissions.AlertRead));
    }
}
