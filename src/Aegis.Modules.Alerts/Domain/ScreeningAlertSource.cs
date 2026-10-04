namespace Aegis.Modules.Alerts.Domain;

/// <summary>Alerts raised by confirmed screening matches have no AML rule; they all carry this fixed rule id.</summary>
public static class ScreeningAlertSource
{
    public static readonly Guid RuleId = new("5c3e0000-0000-4000-8000-000000000001");
    public const string RuleCode = "SCREENING_MATCH";
}
