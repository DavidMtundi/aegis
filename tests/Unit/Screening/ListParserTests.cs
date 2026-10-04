namespace Aegis.Tests.Unit.Screening;

using System.Text;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;

public sealed class ListParserTests
{
    private static Stream Fixture(string name)
        => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "screening", name));

    [Fact]
    public void Un_list_parses_individuals_and_entities()
    {
        using var xml = Fixture("un-sample.xml");
        var result = UnSanctionsParser.Parse(xml);

        Assert.Equal(DateTimeOffset.Parse("2026-10-03T23:00:03.428Z"), result.PublishedAt);
        Assert.Equal(4, result.Subjects.Count);

        var zeph = result.Subjects.Single(s => s.ExternalId == "QDi.901");
        Assert.Equal(WatchlistSource.UN, zeph.Source);
        Assert.Equal(WatchlistCategory.SANCTIONS, zeph.Category);
        Assert.Equal(SubjectKind.INDIVIDUAL, zeph.Kind);
        Assert.Equal(new[] { "ZEPHANIAH OKOTH MBWANA", "Zeph Mbwana" }, zeph.Names);
        Assert.Equal(new[] { new BirthYearRange(1978, 1978) }, zeph.BirthYears);
        Assert.Equal(new[] { "Kenya" }, zeph.Countries);
        Assert.Equal("Al-Qaida · listed 2014-05-20", zeph.Detail);

        var baraka = result.Subjects.Single(s => s.ExternalId == "SOi.902");
        Assert.Equal(new[] { "BARAKA TUMAINI" }, baraka.Names);
        Assert.Equal(new[] { new BirthYearRange(1973, 1975) }, baraka.BirthYears);

        var halima = result.Subjects.Single(s => s.ExternalId == "SOi.903");
        Assert.Equal(new[] { new BirthYearRange(1981, 1981) }, halima.BirthYears);

        var entity = result.Subjects.Single(s => s.ExternalId == "QDe.951");
        Assert.Equal(SubjectKind.ENTITY, entity.Kind);
        Assert.Equal(new[] { "CORAL REEF TRADING COMPANY", "Coral Reef General Traders" }, entity.Names);
    }

    [Fact]
    public void Ofac_list_parses_individuals_and_entities_and_skips_vessels()
    {
        using var xml = Fixture("ofac-sample.xml");
        var result = OfacSdnParser.Parse(xml);

        Assert.Equal(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), result.PublishedAt);
        Assert.Equal(2, result.Subjects.Count);

        var amina = result.Subjects.Single(s => s.ExternalId == "990001");
        Assert.Equal(WatchlistSource.OFAC, amina.Source);
        Assert.Equal(SubjectKind.INDIVIDUAL, amina.Kind);
        Assert.Equal(new[] { "Amina Wanjiru NDEGWA", "Amina NDEGWA-ODHIAMBO" }, amina.Names);
        Assert.Equal(new[] { new BirthYearRange(1968, 1968) }, amina.BirthYears);
        Assert.Equal(new[] { "Kenya" }, amina.Countries);
        Assert.Equal("SDGT", amina.Detail);

        var company = result.Subjects.Single(s => s.ExternalId == "990002");
        Assert.Equal(SubjectKind.ENTITY, company.Kind);
        Assert.Equal("IRAN, NPWMD", company.Detail);
    }

    [Theory]
    [InlineData("10 Dec 1948", 1948, 1948)]
    [InlineData("1948", 1948, 1948)]
    [InlineData("circa 1960", 1960, 1960)]
    [InlineData("1948 to 1950", 1948, 1950)]
    public void Ofac_birth_dates_become_year_ranges(string text, int from, int to)
        => Assert.Equal(new BirthYearRange(from, to), OfacSdnParser.YearRange(text));

    [Fact]
    public void Ofac_birth_date_without_a_year_is_ignored()
        => Assert.Null(OfacSdnParser.YearRange("unknown"));

    [Fact]
    public void Wrong_root_element_is_rejected()
    {
        using var xml = new MemoryStream(Encoding.UTF8.GetBytes("<html></html>"));
        Assert.Throws<FormatException>(() => UnSanctionsParser.Parse(xml));
        xml.Position = 0;
        Assert.Throws<FormatException>(() => OfacSdnParser.Parse(xml));
    }

    [Theory]
    [InlineData("CONSOLIDATED_LIST")]
    [InlineData("sdnList")]
    public void Documents_with_a_dtd_are_rejected(string root)
    {
        var xml = $"<?xml version=\"1.0\"?><!DOCTYPE {root} [<!ENTITY x \"expanded\">]><{root}>&x;</{root}>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        Func<Stream, WatchlistParseResult> parse = root == "sdnList" ? OfacSdnParser.Parse : UnSanctionsParser.Parse;
        Assert.Throws<System.Xml.XmlException>(() => parse(stream));
    }
}
