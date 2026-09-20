// Drops the stored offline shell when the session of this runtime ends: a logout, a tenant switch, or a session the
// account API ended. AuthenticationNavigator raises Leaving once for all three, before the full document navigation starts,
// so this is the single place that has to listen.
//
// The module is imported while the surface is alive, by AppShell on its first interactive render, because the departure
// itself has no time to await an import: the unauthorized path ends the session and navigates in the same step. On
// WebAssembly the imported reference is in-process, so the message is posted synchronously from the Leaving handler and is
// queued in the worker before the document goes away.
//
// The worker stores no identity by construction, and the message is the second guard. A runtime with no worker, or one
// whose import failed, has nothing to clear and says so; an offline shell is an addition and no departure waits for it.

using Microsoft.JSInterop;

namespace Blazor.Client.Bootstrap;

public sealed class OfflineShellCache(IJSRuntime javaScriptRuntime, AuthenticationNavigator authenticationNavigator) : IAsyncDisposable
{
    private const string ModulePath = "./js/offline-shell.js";

    private IJSInProcessObjectReference? _module;
    private bool _subscribed;

    // True once the stored shell has been asked to go, so a departure that raises Leaving and then clears again is one action
    public bool Cleared { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if (_subscribed) authenticationNavigator.Leaving -= Clear;
        _subscribed = false;

        try
        {
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone
        }
    }

    // Imports the module and listens for the end of this runtime's session. Called from the first interactive render, so a
    // prerendering pass never reaches it.
    public async Task AttachAsync()
    {
        if (_subscribed) return;

        authenticationNavigator.Leaving += Clear;
        _subscribed = true;

        try
        {
            _module = await javaScriptRuntime.InvokeAsync<IJSInProcessObjectReference>("import", ModulePath);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // No module, so nothing can be cleared from here; the worker still stores nothing that names a user
        }
    }

    public void Clear()
    {
        if (Cleared || _module is null) return;

        try
        {
            _module.InvokeVoid("clearShell");
            Cleared = true;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
            // The document is going away with the session; the next runtime installs and stores a shell of its own
        }
    }
}
