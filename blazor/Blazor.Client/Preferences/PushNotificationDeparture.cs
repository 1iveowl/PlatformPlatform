// Removes what this device holds for an account that is leaving it: the row the account keeps for this device's
// subscription, the identifier this device stored and the subscription the browser itself made. Without it a browser
// stays subscribed to a row of the account that left, so a notification sent to that account is shown to whoever signs
// in next on the same browser, and the notifications switch reads on for them because it was read from the browser
// rather than from the account.
//
// Every way out of the authenticated surface ends the identity of this runtime, so the one Leaving signal is the whole
// departure table: a logout, a session the account API ended, a tenant switch, and another tab of the same browser
// logging out, logging in as someone else or switching tenant. A reload of the same user is a new document and raises
// nothing, so nothing working is destroyed by one.
//
// The row goes first, through DeleteRowAsync, which SessionTransition awaits before it sends a logout or a tenant switch:
// Leaving is raised after that request succeeded, when the session that owns the row is already revoked or replaced, and
// a push service cannot be relied on to report a subscription the browser let go of, so a row not deleted here stays in
// the account and counts against its limit. The wait is bounded and its outcome ignored, because the user leaves either
// way; a row a failed delete leaves behind is removed when a push service reports it gone. A departure the transition
// does not start (a session the account API ended, another tab) has no valid session to delete with and only forgets.
//
// The module is imported while the surface is alive, by AppShell on its first interactive render, because the departure
// itself has no time to await an import. On WebAssembly the imported reference is in-process, so the call is made
// synchronously from the Leaving handler and the stored identifier is removed before the document goes away.

using Account.Client;
using Account.Features.PushNotifications.Domain;
using Blazor.Client.Bootstrap;
using Microsoft.JSInterop;

namespace Blazor.Client.Preferences;

public sealed class PushNotificationDeparture(
    IJSRuntime javaScriptRuntime,
    AuthenticationNavigator authenticationNavigator,
    PushSubscriptionsClient pushSubscriptionsClient
) : IAsyncDisposable
{
    private const string ModulePath = "./js/push-notifications.js";

    // How long a logout or a tenant switch waits for the row to be deleted before it sends its own request anyway
    public static readonly TimeSpan RowDeletionTimeout = TimeSpan.FromSeconds(1);

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

    // Deletes the account's row for the subscription this device stored, while the session that owns it is still valid.
    // Nothing is deleted when this device stored no identifier, so a device that never turned notifications on sends no
    // request. The browser side is left to Forget, which the departure's Leaving signal runs whether or not this succeeded.
    public async Task DeleteRowAsync()
    {
        if (Forgotten || _module is null) return;

        string? storedSubscriptionId;
        try
        {
            storedSubscriptionId = _module.Invoke<string?>("readStoredSubscriptionId");
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
            return;
        }

        if (!PushSubscriptionId.TryParse(storedSubscriptionId, out var pushSubscriptionId)) return;

        using var timeout = new CancellationTokenSource(RowDeletionTimeout);
        try
        {
            await pushSubscriptionsClient.DeleteSubscriptionAsync(pushSubscriptionId, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // The delete did not answer in time; the departure goes on and the row is left to the push service's report
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
