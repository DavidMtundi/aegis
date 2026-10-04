namespace Aegis.Modules.Screening.Domain;

/// <summary>Column lengths of the screening tables; input is checked against these before it reaches the database.</summary>
public static class ScreeningLimits
{
    public const int ExternalIdMaxLength = 64;

    /// <summary>A listed name or alias; stored as a match's matched name.</summary>
    public const int NameMaxLength = 300;

    public const int DetailMaxLength = 500;
    public const int ReviewReasonMaxLength = 2000;

    public static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
