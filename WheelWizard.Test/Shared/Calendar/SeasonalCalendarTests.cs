using WheelWizard.Shared.Calendar;

namespace WheelWizard.Test.Shared.Calendar;

public class SeasonalCalendarTests
{
    [Theory]
    [InlineData("2026-03-31T23:30:00Z", -8, true)]
    [InlineData("2026-03-31T15:30:00Z", 10, true)]
    [InlineData("2026-04-01T23:30:00Z", 2, false)]
    [InlineData("2026-09-12T12:00:00Z", 2, false)]
    public void UsesEitherLocalOrLondonAprilFirst(string utc, int localOffsetHours, bool expected)
    {
        var time = new FixedTimeProvider(
            DateTimeOffset.Parse(utc),
            TimeZoneInfo.CreateCustomTimeZone("local", TimeSpan.FromHours(localOffsetHours), "local", "local")
        );
        Assert.Equal(expected, new SeasonalCalendar(time).IsAprilFirst);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
