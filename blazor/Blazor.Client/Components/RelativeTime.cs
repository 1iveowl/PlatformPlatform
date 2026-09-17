using System.Globalization;

namespace Blazor.Client.Components;

// A point in time as the React edition's SmartDate shows it: "Just now" under a minute (a time slightly in the future
// included), whole minutes under an hour, whole hours under a day, and the culture's short local date after that. The
// text uses the current UI culture and the date the current culture.
public static class RelativeTime
{
    private const int SecondsPerMinute = 60;
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;

    public static string Format(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsedSeconds = (long)Math.Floor((now - value).TotalSeconds);
        if (elapsedSeconds < SecondsPerMinute) return CommonStrings.JustNow;

        var elapsedMinutes = elapsedSeconds / SecondsPerMinute;
        if (elapsedMinutes < MinutesPerHour) return Count(elapsedMinutes, CommonStrings.MinuteAgo, CommonStrings.MinutesAgo);

        var elapsedHours = elapsedMinutes / MinutesPerHour;
        if (elapsedHours < HoursPerDay) return Count(elapsedHours, CommonStrings.HourAgo, CommonStrings.HoursAgo);

        return value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }

    public static string Format(DateTimeOffset? value, DateTimeOffset now)
    {
        return value is { } time ? Format(time, now) : "";
    }

    private static string Count(long count, string one, string other)
    {
        return string.Format(CultureInfo.CurrentCulture, count == 1 ? one : other, count);
    }
}
