namespace Aegis.Tests.Unit.Screening;

using Aegis.Modules.Screening.Domain;

public sealed class NameMatchingTests
{
    [Fact]
    public void Tokens_drop_diacritics_punctuation_and_titles()
        => Assert.Equal(new[] { "JOSE", "ALVAREZ", "NUNEZ" }, NameNormalizer.Tokens("Dr. José  Álvarez-Núñez"));

    [Fact]
    public void Tokens_drop_company_suffixes()
        => Assert.Equal(new[] { "BANCO", "NACIONAL", "DE", "CUBA" }, NameNormalizer.Tokens("Banco Nacional de Cuba Ltd."));

    [Fact]
    public void Tokens_keep_a_name_made_only_of_noise_words()
        => Assert.Equal(new[] { "THE", "COMPANY" }, NameNormalizer.Tokens("The Company"));

    [Fact]
    public void Tokens_of_blank_name_are_empty()
        => Assert.Empty(NameNormalizer.Tokens("  "));

    [Theory]
    [InlineData("MARTHA", "MARHTA", 0.961)]
    [InlineData("DWAYNE", "DUANE", 0.840)]
    [InlineData("DIXON", "DICKSONX", 0.813)]
    [InlineData("SAME", "SAME", 1.0)]
    [InlineData("ABC", "XYZ", 0.0)]
    public void Jaro_winkler_matches_reference_values(string a, string b, double expected)
        => Assert.Equal(expected, JaroWinkler.Similarity(a, b), 3);

    [Fact]
    public void Score_ignores_name_order()
        => Assert.Equal(1.0, Score("John Mwangi Kamau", "KAMAU John MWANGI"));

    [Fact]
    public void Missing_middle_name_still_scores_above_threshold()
        => Assert.Equal(0.9333, Score("John Kamau", "John Mwangi Kamau"), 4);

    [Fact]
    public void Transliteration_variants_score_above_threshold()
        => Assert.True(Score("Mohammed Ali", "Muhammad Ali") >= WatchlistIndex.DefaultThreshold);

    [Theory]
    [InlineData("John Ng'ang'a Kamau")]
    [InlineData("John Ng’ang’a Kamau")]
    [InlineData("John Ng`ang`a Kamau")]
    public void Apostrophes_are_dropped_not_split(string name)
    {
        Assert.Equal(new[] { "JOHN", "NGANGA", "KAMAU" }, NameNormalizer.Tokens(name));
        Assert.True(Score(name, "John Nganga Kamau") >= WatchlistIndex.DefaultThreshold);
    }

    [Fact]
    public void Different_surname_scores_below_threshold()
        => Assert.True(Score("John Smith", "John Kamau") < 0.8);

    [Fact]
    public void One_shared_token_against_a_longer_name_is_capped_below_threshold()
        => Assert.True(Score("Saad", "Saad Omar Hassan") < WatchlistIndex.DefaultThreshold);

    private static double Score(string a, string b) => NameMatcher.Score(NameNormalizer.Tokens(a), NameNormalizer.Tokens(b));
}
