using Account.Client;
using Account.Features.Users.Requests;
using Blazor.Client.Session;
using Blazor.Client.Shell;
using Microsoft.JSInterop;

namespace Blazor.Client.Preferences;

// The theme as the host's theme.js reports it: the selected mode, the applied light or dark theme and, after a change, the
// mode selected before
public sealed record ThemeState(string Theme, string ResolvedTheme, string? FromTheme = null);

// The zoom level as the host's theme.js reports it and, after a change, the level selected before
public sealed record ZoomState(string ZoomLevel, string? FromZoomLevel = null);

// The theme and zoom level of this device, shared by the shell's user menu and the preferences page, which render in the
// same WebAssembly scope; Changed tells every subscriber when either changes. The host's theme.js owns the decision and
// the storage on every page and applies both before first paint, so this service only reads and changes them through
// wwwroot/js/shell.js and reports each change to the account API as telemetry. A telemetry call's failure is ignored:
// the preference is already applied and stored on the device. The language is not device state: it is saved on the user
// (the preferences page does that) and only remembered here in the preferred-locale cookie for the public pages.
// Registered in both containers; the host never calls it, because it is used only once the component runs in the browser.
public sealed class DevicePreferences(IJSRuntime javaScriptRuntime, IServiceProvider services) : IAsyncDisposable
{
    private const string ModulePath = "./js/shell.js";

    private IJSObjectReference? _module;

    public bool IsLoaded { get; private set; }

    public ThemeMode Theme { get; private set; }

    public string ZoomLevel { get; private set; } = ZoomPreference.DefaultLevel;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone
        }
    }

    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (IsLoaded) return;

        var theme = await InvokeAsync<ThemeState>("readTheme");
        var zoom = await InvokeAsync<ZoomState>("readZoomLevel");
        if (theme is null || zoom is null) return;

        Theme = ThemePreference.Parse(theme.Theme);
        ZoomLevel = ZoomPreference.Parse(zoom.ZoomLevel);
        IsLoaded = true;
        Changed?.Invoke();
    }

    // Returns false when the document could not apply the mode
    public async Task<bool> ChangeThemeAsync(ThemeMode mode)
    {
        if (await InvokeAsync<ThemeState>("setTheme", ThemePreference.Format(mode)) is not { FromTheme: { } fromTheme } change) return false;

        Theme = ThemePreference.Parse(change.Theme);
        Changed?.Invoke();
        var command = new ChangeThemeCommand(fromTheme, change.Theme, change.ResolvedTheme);
        await services.GetRequiredService<SessionState>().UnlessLeavingAsync(cancellationToken => services.GetRequiredService<UsersClient>().ChangeThemeAsync(command, cancellationToken));
        return true;
    }

    // Returns false when the document could not apply the level
    public async Task<bool> ChangeZoomLevelAsync(string level)
    {
        if (await InvokeAsync<ZoomState>("setZoomLevel", ZoomPreference.Parse(level)) is not { FromZoomLevel: { } fromZoomLevel } change) return false;

        ZoomLevel = ZoomPreference.Parse(change.ZoomLevel);
        Changed?.Invoke();
        var command = new ChangeZoomLevelCommand(fromZoomLevel, ZoomLevel);
        await services.GetRequiredService<SessionState>().UnlessLeavingAsync(cancellationToken => services.GetRequiredService<UsersClient>().ChangeZoomLevelAsync(command, cancellationToken));
        return true;
    }

    // Writes the preferred-locale cookie, so the public pages and the next login page render in the language the user saved
    public async Task RememberLocaleAsync(string locale)
    {
        if (LocalePreference.Parse(locale) is not { } supported) return;

        await InvokeAsync<bool>("rememberLocale", supported);
    }

    private async Task<TValue?> InvokeAsync<TValue>(string identifier, params object?[] arguments)
    {
        try
        {
            _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await _module.InvokeAsync<TValue?>(identifier, arguments);
        }
        catch (JSDisconnectedException)
        {
            return default;
        }
    }
}
