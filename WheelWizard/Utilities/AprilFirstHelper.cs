namespace WheelWizard.Utilities;

public static class AprilFirstHelper
{
    private static readonly TimeZoneInfo LondonTimeZone = GetLondonTimeZone();

    public static bool IsAprilFirstLocalOrBst()
    {
        var localNow = DateTime.Now;
        if (localNow.Month == 4 && localNow.Day == 1)
            return true;

        var londonNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LondonTimeZone);
        return londonNow.Month == 4 && londonNow.Day == 1;
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
