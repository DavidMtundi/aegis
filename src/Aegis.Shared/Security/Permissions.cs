namespace Aegis.Shared.Security;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Permission codes from PRD §10.</summary>
public static class Permissions
{
    public const string CustomerRead = "customer.read";
    public const string CustomerWrite = "customer.write";
    public const string TransactionRead = "transaction.read";
    public const string TransactionWrite = "transaction.write";
    public const string AlertRead = "alert.read";
    public const string AlertAssign = "alert.assign";
    public const string AlertDismiss = "alert.dismiss";
    public const string AlertResolve = "alert.resolve";
    public const string AlertEscalate = "alert.escalate";
    public const string CaseRead = "case.read";
    public const string CaseCreate = "case.create";
    public const string CaseUpdate = "case.update";
    public const string CaseClose = "case.close";
    public const string RuleRead = "rule.read";
    public const string RuleCreate = "rule.create";
    public const string RuleActivate = "rule.activate";
    public const string AuditRead = "audit.read";
    public const string UserManage = "user.manage";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CustomerRead, CustomerWrite, TransactionRead, TransactionWrite,
        AlertRead, AlertAssign, AlertDismiss, AlertResolve, AlertEscalate,
        CaseRead, CaseCreate, CaseUpdate, CaseClose,
        RuleRead, RuleCreate, RuleActivate,
        AuditRead, UserManage
    };
}

/// <summary>
/// Built-in role grants. Tenant-configurable roles (the identity.roles table) can replace this later.
/// </summary>
public static class RolePermissions
{
    private static readonly string[] ViewerGrants =
    {
        Permissions.CustomerRead, Permissions.TransactionRead, Permissions.AlertRead,
        Permissions.CaseRead, Permissions.RuleRead
    };

    private static readonly string[] AnalystGrants = ViewerGrants.Concat(new[]
    {
        Permissions.CustomerWrite, Permissions.TransactionWrite,
        Permissions.AlertAssign, Permissions.AlertDismiss, Permissions.AlertResolve, Permissions.AlertEscalate,
        Permissions.CaseCreate, Permissions.CaseUpdate, Permissions.CaseClose
    }).ToArray();

    private static readonly string[] ReviewerGrants = AnalystGrants.Append(Permissions.AuditRead).ToArray();

    private static readonly Dictionary<string, HashSet<string>> RoleGrants = new(StringComparer.OrdinalIgnoreCase)
    {
        [RoleNames.Viewer] = new(ViewerGrants),
        [RoleNames.Analyst] = new(AnalystGrants),
        [RoleNames.Reviewer] = new(ReviewerGrants),
        [RoleNames.Admin] = new(Permissions.All)
    };

    public static bool Grants(IEnumerable<string> roles, string permission)
        => roles.Any(r => RoleGrants.TryGetValue(r, out var granted) && granted.Contains(permission));
}
