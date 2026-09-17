namespace Blazor.Client.Preferences;

// The language a visitor chose on this device, kept in the preferred-locale cookie so the host renders public pages in it
// and a logout keeps it. The host reads it after a signed-in user's locale claim and before Accept-Language
// (HostShell.GetLocale). It is an untrusted hint: only a value naming a supported culture exactly is used, anything else is
// ignored. The cookie has the preferred-tenant cookie's shape (Path=/, Secure, SameSite=Lax, one year) and is not HttpOnly,
// because the public language menu and the preferences page write it from the browser through wwwroot/js/theme.js.
public static class LocalePreference
{
    public const string CookieName = "preferred-locale";

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(365);

    public static IReadOnlyList<string> Locales => SupportedCultures.Locales;

    public static string? Parse(string? cookieValue)
    {
        return SupportedCultures.ToExactSupportedLocale(cookieValue);
    }

    // The language's own name, the same in every culture, as the React edition's locale labels
    public static string Label(string locale)
    {
        return Parse(locale) == "da-DK" ? CommonStrings.LanguageDanish : CommonStrings.LanguageEnglish;
    }
}
