namespace Aegis.Modules.Screening.Domain;

using System.Globalization;
using System.Text;

public static class NameNormalizer
{
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "MR", "MRS", "MS", "MISS", "DR", "PROF", "SIR", "HON", "ENG", "REV",
        "LTD", "LIMITED", "CO", "COMPANY", "INC", "LLC", "PLC", "CORP", "CORPORATION", "SA", "GMBH", "THE"
    };

    /// <summary>Removed rather than split on, so "Ng'ang'a" and "Nganga" are the same token.</summary>
    private static readonly HashSet<char> Apostrophes = new() { '\'', '\u2019', '`' };

    /// <summary>Upper-case tokens without diacritics, punctuation, titles or company suffixes.</summary>
    public static IReadOnlyList<string> Tokens(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark || Apostrophes.Contains(ch)) continue;
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }

        var tokens = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = tokens.Where(t => !Noise.Contains(t)).ToArray();
        return kept.Length > 0 ? kept : tokens;
    }
}
