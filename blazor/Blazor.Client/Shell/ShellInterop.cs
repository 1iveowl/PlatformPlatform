using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Client.Shell;

public sealed record ShellViewport(bool IsWide, bool IsSmall);

public sealed record ShellBrowserState(bool IsWide, bool IsSmall, string? StoredCollapsed);

// The theme as the host's theme.js reports it: the selected mode, the applied light or dark theme and, after a change, the
// mode selected before
public sealed record ShellThemeState(string Theme, string ResolvedTheme, string? FromTheme = null);

// The typed access to wwwroot/js/shell.js for AppShell, UserMenu, MobileMenu and InstallPrompt, including the theme the host's
// js/theme.js applies. The module is imported once
// per wrapper and every handle it returns is disposed with it, which removes the listeners the handle attached. Every call
// tolerates a document that is already gone (a full document navigation leaving the authenticated surface): the shell
// then keeps its defaults, an expanded sidebar and no install prompt.
public sealed class ShellInterop(IJSRuntime javaScriptRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/shell.js";

    private static readonly ShellBrowserState DefaultBrowserState = new(true, false, null);
    private static readonly InstallPromptEnvironment DefaultInstallPromptEnvironment = new(null, 0, false, null, false);

    private readonly List<IJSObjectReference> _handles = [];
    private IJSObjectReference? _module;
    private IJSObjectReference? _shell;

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var handle in _handles)
            {
                await handle.InvokeVoidAsync("dispose");
                await handle.DisposeAsync();
            }

            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone, and its listeners with it
        }

        _handles.Clear();
    }

    public async ValueTask<ShellBrowserState> AttachShellAsync<TComponent>(DotNetObjectReference<TComponent> dotNet) where TComponent : class
    {
        try
        {
            var module = _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _shell = await module.InvokeAsync<IJSObjectReference>("attachShell", dotNet);
            _handles.Add(_shell);
            return await _shell.InvokeAsync<ShellBrowserState>("readState");
        }
        catch (JSDisconnectedException)
        {
            return DefaultBrowserState;
        }
    }

    public async ValueTask StoreCollapsedAsync(bool collapsed)
    {
        if (_shell is null) return;

        try
        {
            await _shell.InvokeVoidAsync("storeCollapsed", ShellSidebarState.FormatStored(collapsed));
        }
        catch (JSDisconnectedException)
        {
            // The document is gone; the choice was not stored
        }
    }

    public async ValueTask<InstallPromptEnvironment> ReadInstallPromptEnvironmentAsync()
    {
        try
        {
            var module = _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await module.InvokeAsync<InstallPromptEnvironment>("readInstallPromptEnvironment");
        }
        catch (JSDisconnectedException)
        {
            return DefaultInstallPromptEnvironment;
        }
    }

    public async ValueTask AttachInstallPromptSwipeAsync<TComponent>(ElementReference element, DotNetObjectReference<TComponent> dotNet) where TComponent : class
    {
        try
        {
            var module = _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _handles.Add(await module.InvokeAsync<IJSObjectReference>("attachInstallPromptSwipe", element, dotNet));
        }
        catch (JSDisconnectedException)
        {
            // The document is gone, and the banner with it
        }
    }

    // A dismissal with an end time lasts until then; without one it lasts for the browser session
    public async ValueTask StoreInstallPromptDismissalAsync(string? dismissedUntil)
    {
        try
        {
            var module = _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await module.InvokeVoidAsync("storeInstallPromptDismissal", dismissedUntil);
        }
        catch (JSDisconnectedException)
        {
            // The document is gone; the dismissal was not stored
        }
    }

    public async ValueTask<ShellThemeState?> SetThemeAsync(ThemeMode mode)
    {
        try
        {
            var module = _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await module.InvokeAsync<ShellThemeState?>("setTheme", ThemePreference.Format(mode));
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    public async ValueTask<ShellThemeState?> ReadThemeAsync()
    {
        try
        {
            var module = _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await module.InvokeAsync<ShellThemeState?>("readTheme");
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    public async ValueTask FocusAsync(string elementId)
    {
        try
        {
            var module = _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await module.InvokeVoidAsync("focusElement", elementId);
        }
        catch (JSDisconnectedException)
        {
            // The document is gone, and the element with it
        }
    }
}
