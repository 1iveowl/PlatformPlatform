using System.Globalization;

namespace SharedKernel.Localization;

// The cultures the platform ships strings for and the order that picks one per request, matching UserInfo and
// SinglePageAppConfiguration on the server: a signed-in user's locale claim decides alone; otherwise the language a visitor
// chose on this device (the preferred-locale cookie) when it names a supported culture exactly; otherwise the first
// Accept-Language entry that matches exactly or by base language; anything else falls back to en-US.
public static class SupportedCultures
{
    public const string DefaultLocale = "en-US";

    public static readonly string[] Locales = [DefaultLocale, "da-DK"];

    public static string SelectLocale(string? claimLocale, string? preferredLocale, IEnumerable<string> acceptLanguagesByPreference)
    {
        if (!string.IsNullOrEmpty(claimLocale)) return ToSupportedLocale(claimLocale) ?? DefaultLocale;

        if (ToExactSupportedLocale(preferredLocale) is { } preferred) return preferred;

        return acceptLanguagesByPreference.Select(ToSupportedLocale).FirstOrDefault(locale => locale is not null) ?? DefaultLocale;
    }

    // A supported locale named exactly, ignoring case, else null; for values the browser stores, which are untrusted hints
    // and never matched by base language
    public static string? ToExactSupportedLocale(string? locale)
    {
        return locale is null ? null : Locales.FirstOrDefault(supported => supported.Equals(locale, StringComparison.OrdinalIgnoreCase));
    }

    // An exact match ignoring case, else the first supported locale with the same two-letter base language
    public static string? ToSupportedLocale(string? locale)
    {
        if (locale is null || locale.Length < 2) return null;

        var exactMatch = Locales.FirstOrDefault(supported => supported.Equals(locale, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null) return exactMatch;

        var baseLanguageCode = locale[..2];
        return Locales.FirstOrDefault(supported => supported.StartsWith(baseLanguageCode, StringComparison.OrdinalIgnoreCase));
    }

    public static CultureInfo GetCulture(string? locale)
    {
        return CultureInfo.GetCultureInfo(ToSupportedLocale(locale) ?? DefaultLocale);
    }
}
