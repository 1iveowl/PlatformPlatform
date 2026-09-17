namespace Blazor.Client.Preferences;

// The language group's state on the preferences page. The current locale is the one the document renders in; a change is
// saved on the user first and only then remembered in the cookie and applied by loading the page again, so one change runs
// at a time: a second choice while one is pending, or the current locale again, starts nothing, and no older response can
// restore an earlier choice. A failed save keeps the current locale selected and the group available for a retry; Revision
// changes so the group renders its choices afresh, since the browser already checked the refused one.
public sealed class LocaleChange(string currentLocale)
{
    public string CurrentLocale { get; } = LocalePreference.Parse(currentLocale) ?? SupportedCultures.DefaultLocale;

    public string? PendingLocale { get; private set; }

    public bool IsPending => PendingLocale is not null;

    public int Revision { get; private set; }

    public bool TryBegin(string requestedLocale)
    {
        if (IsPending || LocalePreference.Parse(requestedLocale) is not { } locale || locale == CurrentLocale) return false;

        PendingLocale = locale;
        return true;
    }

    public void Fail()
    {
        PendingLocale = null;
        Revision++;
    }
}
