namespace Aegis.Tests.Unit.Aml;

using Aegis.Shared.Domain;

public sealed class RuleDurationTests
{
    [Theory]
    [InlineData("30m", 0, 30)]
    [InlineData("24h", 24, 0)]
    [InlineData("7d", 168, 0)]
    [InlineData(" 1H ", 1, 0)]
    public void Parses_minutes_hours_and_days(string text, int hours, int minutes)
    {
        Assert.True(RuleDuration.TryParse(text, out var value));
        Assert.Equal(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0h")]
    [InlineData("10")]
    [InlineData("2w")]
    [InlineData("-1d")]
    public void Rejects_invalid_or_zero_durations(string? text)
    {
        Assert.False(RuleDuration.TryParse(text, out _));
    }
}
