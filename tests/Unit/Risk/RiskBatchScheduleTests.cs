namespace Aegis.Tests.Unit.Risk;

using Aegis.Application.Risk;

public class RiskBatchScheduleTests
{
    [Fact]
    public void Runs_later_today_when_the_hour_has_not_passed()
    {
        var now = new DateTimeOffset(2026, 10, 3, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero), RiskBatchSchedule.NextRun(now, 2));
    }

    [Fact]
    public void Runs_tomorrow_when_the_hour_has_passed_or_is_now()
    {
        var atHour = new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero);
        var after = new DateTimeOffset(2026, 10, 3, 23, 59, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero), RiskBatchSchedule.NextRun(atHour, 2));
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero), RiskBatchSchedule.NextRun(after, 2));
    }

    [Fact]
    public void Converts_non_utc_input_and_clamps_the_hour()
    {
        var nairobi = new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.FromHours(3)); // 01:00 UTC

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero), RiskBatchSchedule.NextRun(nairobi, 2));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero), RiskBatchSchedule.NextRun(nairobi, 99));
    }
}
