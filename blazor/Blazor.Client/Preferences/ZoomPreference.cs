namespace Blazor.Client.Preferences;

// The zoom decision, the React edition's preferences page: the level is stored in the browser under "zoom-level" as one of
// 0.875, 1, 1.125 or 1.25, the default 1 is stored as no value, and a missing or unknown value counts as the default. The
// document applies a level other than the default as data-zoom-level on <html> before first paint, which the host's
// wwwroot/js/theme.js does with the same values; app.css turns the attribute into the --zoom-level variable, so no style
// attribute is written. ZoomPreferenceTests fails when the script and this class drift.
public static class ZoomPreference
{
    public const string StorageKey = "zoom-level";

    public const string DefaultLevel = "1";

    public static IReadOnlyList<string> Levels { get; } = ["0.875", DefaultLevel, "1.125", "1.25"];

    public static string Parse(string? stored)
    {
        return stored is not null && Levels.Contains(stored, StringComparer.Ordinal) ? stored : DefaultLevel;
    }

    public static string Label(string level)
    {
        return Parse(level) switch
        {
            "0.875" => AccountStrings.ZoomSmall,
            "1.125" => AccountStrings.ZoomLarge,
            "1.25" => AccountStrings.ZoomLarger,
            _ => AccountStrings.ZoomDefault
        };
    }
}
