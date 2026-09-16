using System.Globalization;

namespace SharedKernel.Localization;

// The cultures the platform ships strings for and the order that picks one per request, matching UserInfo and
// SinglePageAppConfiguration on the server: a signed-in user's locale claim decides alone, an anonymous visitor gets the
// first Accept-Language entry that matches exactly or by base language, and anything else falls back to en-US.
public static class SupportedCultures
{
    public const string DefaultLocale = "en-US";

    public static readonly string[] Locales = [DefaultLocale, "da-DK"];

    public static string SelectLocale(string? claimLocale, IEnumerable<string> acceptLanguagesByPreference)
    {
        if (!string.IsNullOrEmpty(claimLocale)) return ToSupportedLocale(claimLocale) ?? DefaultLocale;

        return acceptLanguagesByPreference.Select(ToSupportedLocale).FirstOrDefault(locale => locale is not null) ?? DefaultLocale;
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
