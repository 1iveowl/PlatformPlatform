// Whether the shell shows the iOS add-to-home-screen banner, matching the React edition's AddToHomescreen: only on an iOS
// device (an iPhone, iPad or iPod user agent, or an iPadOS desktop user agent with touch points), never in standalone
// display mode, not until the stored dismissal time has passed, and not again in the session after a swipe dismissal.
// The stored value is the time the dismissal ends in unix milliseconds, the format React writes under the same key.
// shell.js reads the browser facts and stores the dismissal; a missing, malformed or implausibly distant stored value
// counts as never dismissed, so a damaged preference never hides the prompt for good.

using System.Globalization;

namespace Blazor.Client.Shell;

public sealed record InstallPromptEnvironment(string? UserAgent, int MaxTouchPoints, bool IsStandalone, string? DismissedUntil, bool DismissedForSession);

public static class InstallPromptPolicy
{
    public const string DismissedStorageKey = "add-to-homescreen-dismissed";

    public const string DismissedForSessionStorageKey = "add-to-homescreen-dismissed_session";

    public static readonly TimeSpan DismissalPeriod = TimeSpan.FromDays(7);

    public static bool IsIos(string? userAgent, int maxTouchPoints)
    {
        if (string.IsNullOrEmpty(userAgent)) return false;
        if (userAgent.Contains("iPhone", StringComparison.Ordinal) || userAgent.Contains("iPad", StringComparison.Ordinal) || userAgent.Contains("iPod", StringComparison.Ordinal)) return true;

        // iPadOS reports a desktop Safari user agent; touch points tell it apart from a Mac
        return userAgent.Contains("Macintosh", StringComparison.Ordinal) && maxTouchPoints > 1;
    }

    public static bool ShouldShow(InstallPromptEnvironment environment, DateTimeOffset now)
    {
        if (!IsIos(environment.UserAgent, environment.MaxTouchPoints) || environment.IsStandalone || environment.DismissedForSession) return false;

        return ParseDismissedUntil(environment.DismissedUntil) is not { } dismissedUntil || dismissedUntil <= now || dismissedUntil > now + DismissalPeriod;
    }

    public static string FormatDismissedUntil(DateTimeOffset now)
    {
        return (now + DismissalPeriod).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDismissedUntil(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)) return null;
        if (milliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()) return null;

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
