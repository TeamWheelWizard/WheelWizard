namespace WheelWizard.Shared.Calendar;

public interface ISeasonalCalendar
{
    bool IsAprilFirst { get; }
}

public sealed class SeasonalCalendar(TimeProvider timeProvider) : ISeasonalCalendar
{
    private static readonly TimeZoneInfo LondonTimeZone = GetLondonTimeZone();

    public bool IsAprilFirst
    {
        get
        {
            var now = timeProvider.GetUtcNow();
            var localNow = TimeZoneInfo.ConvertTime(now, timeProvider.LocalTimeZone);
            if (localNow.Month == 4 && localNow.Day == 1)
                return true;

            var londonNow = TimeZoneInfo.ConvertTime(now, LondonTimeZone);
            return londonNow.Month == 4 && londonNow.Day == 1;
        }
    }

    private static TimeZoneInfo GetLondonTimeZone()
    {
        foreach (var timeZoneId in new[] { "Europe/London", "GMT Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }
}
