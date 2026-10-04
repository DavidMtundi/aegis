namespace Aegis.Tests.Unit.Screening;

using Aegis.Application.Screening;
using Aegis.Modules.Screening.Domain;

public sealed class TenantWatchlistCsvParserTests
{
    private const string Header = "reference,name,category,kind,aliases,birthYear,country,notes\n";

    [Fact]
    public void Parses_rows_with_aliases_ranges_and_quoted_names()
    {
        var result = TenantWatchlistCsvParser.Parse(Header +
            "PEP-1,\"Wanjiku, Jane Doe\",pep,individual,Jane W. Doe; J. Wanjiku,1968-1970,KE,Former governor\n" +
            "INT-7,Blue Lagoon Traders,WATCHLIST,ENTITY,,,,\n");

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Subjects.Count);
        var pep = result.Subjects[0];
        Assert.Equal(WatchlistSource.TENANT, pep.Source);
        Assert.Equal("PEP-1", pep.ExternalId);
        Assert.Equal(WatchlistCategory.PEP, pep.Category);
        Assert.Equal(SubjectKind.INDIVIDUAL, pep.Kind);
        Assert.Equal(new[] { "Wanjiku, Jane Doe", "Jane W. Doe", "J. Wanjiku" }, pep.Names);
        Assert.Equal(new[] { new BirthYearRange(1968, 1970) }, pep.BirthYears);
        Assert.Equal(new[] { "KE" }, pep.Countries);
        Assert.Equal("Former governor", pep.Detail);
        Assert.Empty(result.Subjects[1].BirthYears);
    }

    [Fact]
    public void Missing_required_column_is_a_header_error()
    {
        var result = TenantWatchlistCsvParser.Parse("reference,name,kind\nA,B,INDIVIDUAL\n");
        Assert.Equal("Missing required columns: category.", Assert.Single(result.Errors));
        Assert.Empty(result.Subjects);
    }

    [Fact]
    public void Any_bad_row_rejects_the_whole_file()
    {
        var result = TenantWatchlistCsvParser.Parse(Header +
            "OK-1,Good Row,PEP,INDIVIDUAL,,,,\n" +
            "BAD-1,Bad Row,FRIEND,PERSON,,19x0,,\n" +
            "OK-1,Duplicate,PEP,INDIVIDUAL,,,,\n");

        Assert.Empty(result.Subjects);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(
            "Line 3: category must be PEP, SANCTIONS or WATCHLIST; kind must be INDIVIDUAL or ENTITY; birthYear must be a year (1970) or a range (1968-1972).",
            result.Errors[0]);
        Assert.Equal("Line 4: reference 'OK-1' appears more than once.", result.Errors[1]);
    }

    [Fact]
    public void File_with_only_a_header_is_rejected()
        => Assert.Equal("The file has no rows.", Assert.Single(TenantWatchlistCsvParser.Parse(Header).Errors));
}
