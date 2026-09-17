namespace Blazor.Client.Shell;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum AppliedTheme
{
    Light,
    Dark
}

// The theme decision, the React edition's next-themes behaviour: the selected mode is stored in the browser under "theme"
// as system, light or dark, a missing or unknown value counts as system, and system follows the device's
// prefers-color-scheme. The document applies the result as data-theme on <html> before first paint, which the host's
// wwwroot/js/theme.js does with the same values; ThemePreferenceTests fails when the two drift.
public static class ThemePreference
{
    public const string StorageKey = "theme";

    public static IReadOnlyList<ThemeMode> Modes { get; } = [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];

    public static ThemeMode Parse(string? stored)
    {
        return stored switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System
        };
    }

    public static AppliedTheme Resolve(ThemeMode mode, bool prefersDark)
    {
        return mode switch
        {
            ThemeMode.Light => AppliedTheme.Light,
            ThemeMode.Dark => AppliedTheme.Dark,
            _ => prefersDark ? AppliedTheme.Dark : AppliedTheme.Light
        };
    }

    public static AppliedTheme Resolve(string? stored, bool prefersDark)
    {
        return Resolve(Parse(stored), prefersDark);
    }

    public static string Format(ThemeMode mode)
    {
        return mode.ToString().ToLowerInvariant();
    }

    public static string Format(AppliedTheme theme)
    {
        return theme.ToString().ToLowerInvariant();
    }
}
