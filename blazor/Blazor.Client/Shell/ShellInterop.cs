using Blazor.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Client.Shell;

public sealed record ShellViewport(bool IsWide, bool IsSmall);

public sealed record ShellBrowserState(bool IsWide, bool IsSmall, string? StoredCollapsed);

// The typed access to wwwroot/js/shell.js for AppShell, UserMenu, MobileMenu and InstallPrompt; the theme and the zoom level
// go through DevicePreferences. The module is imported once per wrapper, however many calls ask for it at the same time, and
// every handle it returns is disposed with it, which removes the listeners the handle attached. The owner can be torn down
// while an import or an attach is still in flight: DisposeAsync waits for those to settle and then releases every handle and
// the module once, so a late handle never keeps its listeners and the owner can release its .NET reference after it. Every
// call made after teardown does nothing. Every call tolerates a document that is already gone (a full document navigation
// leaving the authenticated surface): the shell then keeps its defaults, an expanded sidebar and no install prompt.
public sealed class ShellInterop(IJSRuntime javaScriptRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/shell.js";

    private static readonly ShellBrowserState DefaultBrowserState = new(true, false, null);
    private static readonly InstallPromptEnvironment DefaultInstallPromptEnvironment = new(null, 0, false, null, false);

    private readonly List<IJSObjectReference> _handles = [];
    private readonly List<Task> _inFlight = [];
    private Task<IJSObjectReference>? _import;
    private bool _isDisposed;
    private IJSObjectReference? _shell;

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _isDisposed = true;

        // Each call in flight reports its own failure to its caller; here it only has to settle, so what it acquired is released below
        await Task.WhenAll(_inFlight).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

        IJSObjectReference[] handles = [.. _handles];
        _handles.Clear();
        _shell = null;
        try
        {
            foreach (var handle in handles)
            {
                await JavaScriptRelease.HandleAsync(handle);
            }
        }
        finally
        {
            if (_import is { IsCompletedSuccessfully: true }) await JavaScriptRelease.ReferenceAsync(_import.Result);
        }
    }

    public async ValueTask<ShellBrowserState> AttachShellAsync<TComponent>(DotNetObjectReference<TComponent> dotNet) where TComponent : class
    {
        try
        {
            if (await AttachAsync("attachShell", dotNet) is not { } shell) return DefaultBrowserState;

            _shell = shell;
            return await shell.InvokeAsync<ShellBrowserState>("readState");
        }
        catch (JSDisconnectedException)
        {
            return DefaultBrowserState;
        }
    }

    public async ValueTask StoreCollapsedAsync(bool collapsed)
    {
        if (_isDisposed || _shell is null) return;

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
            if (await ModuleAsync() is not { } module) return DefaultInstallPromptEnvironment;

            return await module.InvokeAsync<InstallPromptEnvironment>("readInstallPromptEnvironment");
        }
        catch (JSDisconnectedException)
        {
            return DefaultInstallPromptEnvironment;
        }
    }

    public async ValueTask AttachInstallPromptSwipeAsync<TComponent>(ElementReference element, DotNetObjectReference<TComponent> dotNet) where TComponent : class
    {
        await AttachAsync("attachInstallPromptSwipe", element, dotNet);
    }

    // A dismissal with an end time lasts until then; without one it lasts for the browser session
    public async ValueTask StoreInstallPromptDismissalAsync(string? dismissedUntil)
    {
        try
        {
            if (await ModuleAsync() is not { } module) return;

            await module.InvokeVoidAsync("storeInstallPromptDismissal", dismissedUntil);
        }
        catch (JSDisconnectedException)
        {
            // The document is gone; the dismissal was not stored
        }
    }

    public async ValueTask FocusAsync(string elementId)
    {
        try
        {
            if (await ModuleAsync() is not { } module) return;

            await module.InvokeVoidAsync("focusElement", elementId);
        }
        catch (JSDisconnectedException)
        {
            // The document is gone, and the element with it
        }
    }

    // The one import every call shares; null once the wrapper is torn down, also when the import completes after that, and
    // when the document is already gone
    private async Task<IJSObjectReference?> ModuleAsync()
    {
        if (_isDisposed) return null;

        try
        {
            if (_import is null)
            {
                _import = javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();
                _inFlight.Add(_import);
            }

            var module = await _import;
            return _isDisposed ? null : module;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    // A handle that arrives after teardown is still kept, so DisposeAsync, which waits for this call, releases it
    private Task<IJSObjectReference?> AttachAsync(string identifier, params object?[] arguments)
    {
        var attach = AttachOnceAsync(identifier, arguments);
        _inFlight.Add(attach);
        return attach;
    }

    private async Task<IJSObjectReference?> AttachOnceAsync(string identifier, object?[] arguments)
    {
        if (await ModuleAsync() is not { } module) return null;

        try
        {
            var handle = await module.InvokeAsync<IJSObjectReference>(identifier, arguments);
            _handles.Add(handle);
            return _isDisposed ? null : handle;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }
}
