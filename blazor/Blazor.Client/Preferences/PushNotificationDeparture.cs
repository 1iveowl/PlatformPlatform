// Forgets what this browser holds for an account that is leaving the device: the identifier this device stored and the
// subscription the browser itself made. Without it a browser stays subscribed to a row of the account that left, so a
// notification sent to that account is shown to whoever signs in next on the same browser, and the notifications switch
// reads on for them because it was read from the browser rather than from the account.
//
// Every way out of the authenticated surface ends the identity of this runtime, so the one Leaving signal is the whole
// departure table: a logout, a session the account API ended, a tenant switch, and another tab of the same browser
// logging out, logging in as someone else or switching tenant. A reload of the same user is a new document and raises
// nothing, so nothing working is destroyed by one.
//
// The module is imported while the surface is alive, by AppShell on its first interactive render, because the departure
// itself has no time to await an import. On WebAssembly the imported reference is in-process, so the call is made
// synchronously from the Leaving handler and the stored identifier is removed before the document goes away.

using Blazor.Client.Bootstrap;
using Microsoft.JSInterop;

namespace Blazor.Client.Preferences;

public sealed class PushNotificationDeparture(IJSRuntime javaScriptRuntime, AuthenticationNavigator authenticationNavigator) : IAsyncDisposable
{
    private const string ModulePath = "./js/push-notifications.js";

    private IJSInProcessObjectReference? _module;
    private bool _subscribed;

    // True once this browser has been asked to forget, so a departure that raises Leaving and then leaves is one action
    public bool Forgotten { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if (_subscribed) authenticationNavigator.Leaving -= Forget;
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
    // prerendering pass never reaches it. It listens whether or not this deployment offers notifications, because what it
    // forgets is what a previous deployment or a previous account left in this browser, which is there either way.
    public async Task AttachAsync()
    {
        if (_subscribed) return;

        authenticationNavigator.Leaving += Forget;
        _subscribed = true;

        try
        {
            _module = await javaScriptRuntime.InvokeAsync<IJSInProcessObjectReference>("import", ModulePath);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // Without the module nothing can be forgotten from here; the next account to read the section unsubscribes a
            // subscription it does not own
        }
    }

    public void Forget()
    {
        if (Forgotten || _module is null) return;

        try
        {
            _module.InvokeVoid("forgetDevice");
            Forgotten = true;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
            // The document is going away with the session; what is left is unsubscribed when the section is next read
        }
    }
}
