using System.Text.RegularExpressions;

namespace Blazor.Client.Sessions;

// The browser and operating system a session's user agent names, as the React edition's sessions page parses them, with
// the patterns ordered so the more specific token wins: Edge and Opera also send Chrome, Chrome also sends Safari, and
// Android and iOS also send Linux and Mac OS X. Null means the user agent names none of them.
public sealed record ParsedUserAgent(string? Browser, string? OperatingSystem);

public static partial class UserAgentParser
{
    private static readonly (Regex Pattern, string Name)[] BrowserPatterns =
    [
        (EdgePattern(), "Edge"),
        (OperaPattern(), "Opera"),
        (ChromePattern(), "Chrome"),
        (FirefoxPattern(), "Firefox"),
        (SafariPattern(), "Safari")
    ];

    private static readonly (Regex Pattern, string Name)[] OperatingSystemPatterns =
    [
        (WindowsPattern(), "Windows"),
        (AndroidPattern(), "Android"),
        (IosPattern(), "iOS"),
        (MacPattern(), "macOS"),
        (LinuxPattern(), "Linux")
    ];

    public static ParsedUserAgent Parse(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return new ParsedUserAgent(null, null);

        return new ParsedUserAgent(FirstMatch(BrowserPatterns, userAgent), FirstMatch(OperatingSystemPatterns, userAgent));
    }

    private static string? FirstMatch((Regex Pattern, string Name)[] patterns, string userAgent)
    {
        foreach (var (pattern, name) in patterns)
        {
            if (pattern.IsMatch(userAgent)) return name;
        }

        return null;
    }

    [GeneratedRegex(@"Edg/[\d.]+")]
    private static partial Regex EdgePattern();

    [GeneratedRegex(@"OPR/[\d.]+")]
    private static partial Regex OperaPattern();

    [GeneratedRegex(@"Chrome/[\d.]+")]
    private static partial Regex ChromePattern();

    [GeneratedRegex(@"Firefox/[\d.]+")]
    private static partial Regex FirefoxPattern();

    [GeneratedRegex(@"Safari/[\d.]+")]
    private static partial Regex SafariPattern();

    [GeneratedRegex("Windows NT")]
    private static partial Regex WindowsPattern();

    [GeneratedRegex("Android")]
    private static partial Regex AndroidPattern();

    [GeneratedRegex("iPhone|iPad")]
    private static partial Regex IosPattern();

    [GeneratedRegex("Mac OS X")]
    private static partial Regex MacPattern();

    [GeneratedRegex("Linux")]
    private static partial Regex LinuxPattern();
}
