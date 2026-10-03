namespace Aegis.Shared.Domain;

using System;
using System.Globalization;

/// <summary>Rule schedule durations such as "30m", "24h" or "7d".</summary>
public static class RuleDuration
{
    public static readonly TimeSpan MaxLookback = TimeSpan.FromDays(90);

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2) return false;

        if (!int.TryParse(trimmed[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return false;

        value = char.ToLowerInvariant(trimmed[^1]) switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => TimeSpan.Zero
        };
        return value > TimeSpan.Zero;
    }
}
