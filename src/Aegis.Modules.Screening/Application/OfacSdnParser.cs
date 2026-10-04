namespace Aegis.Modules.Screening.Application;

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aegis.Modules.Screening.Domain;

/// <summary>US Treasury OFAC Specially Designated Nationals list (SDN.XML).</summary>
public static class OfacSdnParser
{
    private static readonly Regex Year = new(@"\b(1[89]\d{2}|20\d{2})\b", RegexOptions.Compiled);

    public static WatchlistParseResult Parse(Stream xml)
    {
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name.LocalName != "sdnList")
            throw new FormatException("Not an OFAC SDN list.");
        var ns = root.Name.Namespace;

        string? Text(XElement e, string name)
        {
            var value = e.Element(ns + name)?.Value.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        string? Name(XElement e)
        {
            var name = string.Join(" ", new[] { Text(e, "firstName"), Text(e, "lastName") }.Where(v => v is not null));
            return name.Length == 0 ? null : name;
        }

        IEnumerable<XElement> Items(XElement e, string list, string item)
            => e.Element(ns + list)?.Elements(ns + item) ?? Enumerable.Empty<XElement>();

        var publishInfo = root.Element(ns + "publshInformation");
        DateTimeOffset? published = DateTime.TryParseExact(
            publishInfo?.Element(ns + "Publish_Date")?.Value, "MM/dd/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var p)
            ? new DateTimeOffset(p, TimeSpan.Zero)
            : null;

        var subjects = new List<WatchlistSubject>();
        foreach (var e in root.Elements(ns + "sdnEntry"))
        {
            SubjectKind? kind = Text(e, "sdnType") switch
            {
                "Individual" => SubjectKind.INDIVIDUAL,
                "Entity" => SubjectKind.ENTITY,
                _ => null
            };
            var uid = Text(e, "uid");
            var primary = Name(e);
            if (kind is null || uid is null || primary is null) continue;

            var names = new List<string> { primary };
            foreach (var aka in Items(e, "akaList", "aka"))
            {
                // OFAC marks low-quality aliases as "weak" and advises against screening on them.
                if (string.Equals(Text(aka, "category"), "weak", StringComparison.OrdinalIgnoreCase)) continue;
                if (Name(aka) is { } alias && !names.Contains(alias, StringComparer.OrdinalIgnoreCase)) names.Add(alias);
            }

            var birthYears = Items(e, "dateOfBirthList", "dateOfBirthItem")
                .Select(d => YearRange(Text(d, "dateOfBirth")))
                .OfType<BirthYearRange>()
                .ToList();
            var countries = Items(e, "nationalityList", "nationality")
                .Concat(Items(e, "citizenshipList", "citizenship"))
                .Select(n => Text(n, "country"))
                .OfType<string>()
                .Distinct()
                .ToList();
            var programs = Items(e, "programList", "program").Select(x => x.Value.Trim()).Where(x => x.Length > 0).ToList();

            subjects.Add(new WatchlistSubject(
                WatchlistSource.OFAC, uid, WatchlistCategory.SANCTIONS, kind.Value, names, birthYears, countries,
                programs.Count == 0 ? null : ScreeningLimits.Truncate(string.Join(", ", programs), ScreeningLimits.DetailMaxLength)));
        }

        return new WatchlistParseResult(subjects, published);
    }

    /// <summary>"10 Dec 1948", "circa 1948" and "1948 to 1950" all become year ranges; text without a year is null.</summary>
    public static BirthYearRange? YearRange(string? text)
    {
        if (text is null) return null;
        var years = Year.Matches(text).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        return years.Count == 0 ? null : new BirthYearRange(years.Min(), years.Max());
    }
}
