namespace Aegis.Application.Screening;

using System.Globalization;
using Aegis.Application.Csv;
using Aegis.Modules.Screening.Domain;

public sealed record TenantWatchlistParseResult(IReadOnlyList<string> Errors, IReadOnlyList<WatchlistSubject> Subjects);

/// <summary>
/// The institution's own list (PEPs, the Kenya domestic list, internal blacklists). All-or-nothing: any row
/// error rejects the file, so a list is never half-replaced.
/// </summary>
public static class TenantWatchlistCsvParser
{
    public const int MaxRows = 10_000;

    private static readonly string[] Required = { "reference", "name", "category", "kind" };

    public static TenantWatchlistParseResult Parse(string csv)
    {
        var lines = CsvLine.Lines(csv);
        var headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0) return Fail("CSV is empty.");

        var header = CsvLine.Split(lines[headerIndex]);
        if (header is null) return Fail("Header row has an unterminated quote.");

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++) columns.TryAdd(header[i].Trim(), i);
        var missing = Required.Where(r => !columns.ContainsKey(r)).ToList();
        if (missing.Count > 0) return Fail($"Missing required columns: {string.Join(", ", missing)}.");

        var errors = new List<string>();
        var subjects = new List<WatchlistSubject>();
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = 0;

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            if (++rows > MaxRows)
            {
                errors.Add($"More than {MaxRows} rows.");
                break;
            }

            var lineNumber = i + 1;
            var fields = CsvLine.Split(lines[i]);
            if (fields is null)
            {
                errors.Add($"Line {lineNumber}: unterminated quote.");
                continue;
            }
            if (fields.Count != header.Count)
            {
                errors.Add($"Line {lineNumber}: expected {header.Count} columns but found {fields.Count}.");
                continue;
            }

            string? Get(string name)
            {
                if (!columns.TryGetValue(name, out var idx) || idx >= fields.Count) return null;
                var value = fields[idx].Trim();
                return value.Length == 0 ? null : value;
            }

            var rowErrors = new List<string>();
            var reference = Get("reference");
            var name = Get("name");
            var aliases = (Get("aliases") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var notes = Get("notes");
            if (reference is null) rowErrors.Add("reference is required");
            else if (reference.Length > ScreeningLimits.ExternalIdMaxLength) rowErrors.Add(TooLong("reference", ScreeningLimits.ExternalIdMaxLength));
            else if (!references.Add(reference)) rowErrors.Add($"reference '{reference}' appears more than once");
            if (name is null) rowErrors.Add("name is required");
            else if (name.Length > ScreeningLimits.NameMaxLength) rowErrors.Add(TooLong("name", ScreeningLimits.NameMaxLength));
            if (aliases.Any(a => a.Length > ScreeningLimits.NameMaxLength)) rowErrors.Add(TooLong("an alias", ScreeningLimits.NameMaxLength));
            if (notes?.Length > ScreeningLimits.DetailMaxLength) rowErrors.Add(TooLong("notes", ScreeningLimits.DetailMaxLength));
            if (!TryParseName<WatchlistCategory>(Get("category"), out var category))
                rowErrors.Add("category must be PEP, SANCTIONS or WATCHLIST");
            if (!TryParseName<SubjectKind>(Get("kind"), out var kind))
                rowErrors.Add("kind must be INDIVIDUAL or ENTITY");

            var birthYears = new List<BirthYearRange>();
            if (Get("birthYear") is { } birthYear)
            {
                if (ParseYears(birthYear) is { } range) birthYears.Add(range);
                else rowErrors.Add("birthYear must be a year (1970) or a range (1968-1972)");
            }

            if (rowErrors.Count > 0)
            {
                errors.Add($"Line {lineNumber}: {string.Join("; ", rowErrors)}.");
                continue;
            }

            var names = new List<string> { name! };
            foreach (var alias in aliases)
                if (!names.Contains(alias, StringComparer.OrdinalIgnoreCase)) names.Add(alias);
            var countries = Get("country") is { } country ? new[] { country } : Array.Empty<string>();

            subjects.Add(new WatchlistSubject(
                WatchlistSource.TENANT, reference!, category, kind, names, birthYears, countries, notes));
        }

        if (errors.Count == 0 && subjects.Count == 0) errors.Add("The file has no rows.");
        return errors.Count > 0
            ? new TenantWatchlistParseResult(errors, Array.Empty<WatchlistSubject>())
            : new TenantWatchlistParseResult(Array.Empty<string>(), subjects);
    }

    /// <summary>Enum names only; <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> also accepts numbers and comma lists.</summary>
    private static bool TryParseName<T>(string? text, out T value) where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
        value = name is null ? default : Enum.Parse<T>(name);
        return name is not null;
    }

    private static BirthYearRange? ParseYears(string text)
    {
        var parts = text.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length > 2) return null;
        var years = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 1900 or > 2100)
                return null;
            years.Add(year);
        }
        return new BirthYearRange(years.Min(), years.Max());
    }

    private static string TooLong(string what, int maxLength) => $"{what} is longer than {maxLength} characters";

    private static TenantWatchlistParseResult Fail(string error)
        => new(new[] { error }, Array.Empty<WatchlistSubject>());
}
