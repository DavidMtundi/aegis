namespace Aegis.Tests.Unit.Dashboard;

using Aegis.Application.Dashboard;
using Aegis.Shared.Domain;

public class DashboardMathTests
{
    [Fact]
    public void Trend_covers_each_day_ending_today_with_zero_fill()
    {
        var now = new DateTimeOffset(2026, 10, 3, 22, 0, 0, TimeSpan.FromHours(-5)); // 2026-10-04 03:00 UTC
        var counts = new Dictionary<DateOnly, int> { [new DateOnly(2026, 10, 4)] = 4, [new DateOnly(2026, 10, 2)] = 1 };

        var trend = DashboardMath.FillTrend(counts, now, 3);

        Assert.Equal(new[] { new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4) }, trend.Select(t => t.Date));
        Assert.Equal(new[] { 1, 0, 4 }, trend.Select(t => t.Count));
    }

    [Fact]
    public void Rate_handles_zero_totals_and_rounds()
    {
        Assert.Equal(0, DashboardMath.Rate(0, 0));
        Assert.Equal(0.3333, DashboardMath.Rate(1, 3));
    }

    [Fact]
    public void All_keys_includes_missing_enum_values()
    {
        var result = DashboardMath.AllKeys(new Dictionary<AlertSeverity, int> { [AlertSeverity.HIGH] = 2 });

        Assert.Equal(4, result.Count);
        Assert.Equal(2, result["HIGH"]);
        Assert.Equal(0, result["LOW"]);
    }
}
