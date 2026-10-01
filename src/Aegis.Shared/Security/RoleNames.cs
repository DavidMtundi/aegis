namespace Aegis.Shared.Security;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Reviewer = "Reviewer";
    public const string Analyst = "Analyst";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Admin, Reviewer, Analyst, Viewer };
}
