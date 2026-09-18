using System.Globalization;
using Account.Client;
using Account.Features.Users.Requests;
using Blazor.Client.Forms;
using Blazor.Client.Session;
using Microsoft.AspNetCore.Components;

namespace Blazor.Client.Preferences;

// The language a signed-in user reads the application in, shared by the preferences page and the shell's mobile menu,
// which render in the same scope and must not start two changes at once. LocaleChange holds the decision: one change at a
// time, the current language again starts nothing, and a refused change keeps the current language selected and allows a
// retry. The change is saved on the user first, then remembered in the preferred-locale cookie for the public pages, and
// only then is the page loaded again, so the host renders the new culture from the refreshed locale claim. Changed tells
// every subscriber that the pending state or the revision moved. Registered in both containers; the host never starts a
// change, because a change only happens once the component runs in the browser.
public sealed class LocaleSwitch(IServiceProvider services, DevicePreferences preferences, NavigationManager navigation)
{
    private readonly LocaleChange _change = new(CultureInfo.CurrentUICulture.Name);

    public string CurrentLocale => _change.CurrentLocale;

    public bool IsPending => _change.IsPending;

    // Changes whenever a refused change leaves the chooser showing a language the browser already selected
    public int Revision => _change.Revision;

    public event Action? Changed;

    // Returns false when the language is the current one or a change is already running
    public async Task<bool> ChangeAsync(string locale)
    {
        if (!_change.TryBegin(locale)) return false;

        Changed?.Invoke();

        // A 401 is handled by the HttpClient's unauthorized handler, which leaves the runtime
        var session = services.GetRequiredService<SessionState>();
        var result = await session.UnlessLeavingAsync(cancellationToken =>
            services.GetRequiredService<UsersClient>().ChangeLocaleAsync(new ChangeLocaleCommand(_change.PendingLocale!), cancellationToken)
        );
        if (result is null) return true;

        if (!result.IsSuccess)
        {
            _change.Fail();
            Changed?.Invoke();
            if (result.Outcome != ApiCallOutcome.Unauthorized) services.GetRequiredService<ApiFailurePresenter>().Present(result);
            return true;
        }

        await preferences.RememberLocaleAsync(_change.PendingLocale!);
        navigation.NavigateTo(navigation.Uri, true);
        return true;
    }
}
