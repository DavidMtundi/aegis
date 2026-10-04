namespace Aegis.Modules.Screening.Application;

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Aegis.Modules.Screening.Domain;

/// <summary>UN Security Council consolidated list (scsanctions.un.org consolidated.xml).</summary>
public static class UnSanctionsParser
{
    public static WatchlistParseResult Parse(Stream xml)
    {
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name.LocalName != "CONSOLIDATED_LIST")
            throw new FormatException("Not a UN consolidated list.");

        DateTimeOffset? published = DateTimeOffset.TryParse(
            (string?)root.Attribute("dateGenerated"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p)
            ? p
            : null;

        var subjects = new List<WatchlistSubject>();
        foreach (var e in root.Element("INDIVIDUALS")?.Elements("INDIVIDUAL") ?? Enumerable.Empty<XElement>())
        {
            var primary = Join(e, "FIRST_NAME", "SECOND_NAME", "THIRD_NAME", "FOURTH_NAME");
            if (Build(e, SubjectKind.INDIVIDUAL, primary, "INDIVIDUAL_ALIAS", BirthYears(e), Values(e.Element("NATIONALITY"))) is { } s)
                subjects.Add(s);
        }
        foreach (var e in root.Element("ENTITIES")?.Elements("ENTITY") ?? Enumerable.Empty<XElement>())
        {
            if (Build(e, SubjectKind.ENTITY, Text(e, "FIRST_NAME"), "ENTITY_ALIAS", Array.Empty<BirthYearRange>(), Array.Empty<string>()) is { } s)
                subjects.Add(s);
        }

        return new WatchlistParseResult(subjects, published);
    }

    private static WatchlistSubject? Build(
        XElement e, SubjectKind kind, string? primary, string aliasElement,
        IReadOnlyList<BirthYearRange> birthYears, IReadOnlyList<string> countries)
    {
        var reference = Text(e, "REFERENCE_NUMBER") ?? Text(e, "DATAID");
        if (primary is null || reference is null) return null;

        var names = new List<string> { primary };
        foreach (var alias in e.Elements(aliasElement))
        {
            // The UN advises against screening on "Low" quality aliases.
            if (string.Equals(Text(alias, "QUALITY"), "Low", StringComparison.OrdinalIgnoreCase)) continue;
            if (Text(alias, "ALIAS_NAME") is { } name && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        var detailParts = new[] { Text(e, "UN_LIST_TYPE"), Text(e, "LISTED_ON") is { } on ? $"listed {on}" : null };
        var detail = string.Join(" · ", detailParts.Where(d => d is not null));
        return new WatchlistSubject(
            WatchlistSource.UN, reference, WatchlistCategory.SANCTIONS, kind, names, birthYears, countries,
            detail.Length == 0 ? null : ScreeningLimits.Truncate(detail, ScreeningLimits.DetailMaxLength));
    }

    private static IReadOnlyList<BirthYearRange> BirthYears(XElement e)
    {
        var ranges = new List<BirthYearRange>();
        foreach (var d in e.Elements("INDIVIDUAL_DATE_OF_BIRTH"))
        {
            if (DateOnly.TryParse(Text(d, "DATE"), CultureInfo.InvariantCulture, out var date))
                ranges.Add(new BirthYearRange(date.Year, date.Year));
            else if (int.TryParse(Text(d, "YEAR"), CultureInfo.InvariantCulture, out var year))
                ranges.Add(new BirthYearRange(year, year));
            else if (int.TryParse(Text(d, "FROM_YEAR"), CultureInfo.InvariantCulture, out var from)
                     && int.TryParse(Text(d, "TO_YEAR"), CultureInfo.InvariantCulture, out var to))
                ranges.Add(new BirthYearRange(Math.Min(from, to), Math.Max(from, to)));
        }
        return ranges;
    }

    private static string? Text(XElement e, string name)
    {
        var value = e.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? Join(XElement e, params string[] parts)
    {
        var joined = string.Join(" ", parts.Select(p => Text(e, p)).Where(v => v is not null));
        return joined.Length == 0 ? null : joined;
    }

    private static IReadOnlyList<string> Values(XElement? e)
        => e?.Elements("VALUE").Select(v => v.Value.Trim()).Where(v => v.Length > 0).ToList() ?? new List<string>();
}
