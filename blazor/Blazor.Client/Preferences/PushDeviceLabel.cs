// The label the user reads beside a subscription, derived from the browser's user agent. It names the browser and the
// platform and nothing else: no version, no build and nothing that identifies a person or a device. An agent this does
// not recognise is labelled as an unknown browser rather than repeated verbatim, so no agent string is ever stored.

using System.Globalization;

namespace Blazor.Client.Preferences;

public static class PushDeviceLabel
{
    // Ordered, because a user agent names more than one of these: Edge and Opera both claim Chrome, and Chrome claims
    // Safari. The first match that is not claimed by a later browser wins.
    private static readonly (string Token, string Name)[] Browsers =
    [
        ("Edg/", "Edge"),
        ("OPR/", "Opera"),
        ("Firefox/", "Firefox"),
        ("Chrome/", "Chrome"),
        ("Safari/", "Safari")
    ];

    private static readonly (string Token, string Name)[] Platforms =
    [
        ("Windows", "Windows"),
        ("Android", "Android"),
        ("iPhone", "iOS"),
        ("iPad", "iPadOS"),
        ("Mac OS X", "macOS"),
        ("CrOS", "ChromeOS"),
        ("Linux", "Linux")
    ];

    public static string Create(string? userAgent)
    {
        var browser = Match(userAgent, Browsers) ?? AccountStrings.NotificationsUnknownBrowser;
        var platform = Match(userAgent, Platforms);

        return platform is null ? browser : string.Format(CultureInfo.CurrentCulture, AccountStrings.NotificationsDeviceLabel, browser, platform);
    }

    private static string? Match(string? userAgent, (string Token, string Name)[] candidates)
    {
        if (string.IsNullOrEmpty(userAgent)) return null;

        foreach (var candidate in candidates)
        {
            if (userAgent.Contains(candidate.Token, StringComparison.Ordinal)) return candidate.Name;
        }

        return null;
    }
}
