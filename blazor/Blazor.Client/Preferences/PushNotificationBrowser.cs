// This browser's side of push notifications, over wwwroot/js/push-notifications.js: what the browser supports, what the
// user has allowed, the subscription itself, and the identifier of the row the account API saved for it. The service
// reports facts and performs the two actions the section asks for; PushNotificationSection decides what is rendered from
// them, and the component makes the account API calls, as every other interactive surface of this edition does.
//
// Registered in both containers; the host never calls it, because it is used only once the component runs in the browser.

using Microsoft.JSInterop;

namespace Blazor.Client.Preferences;

// The browser's subscription as the push protocol defines it: the push service address and the two base64url keys a
// payload is encrypted with
public sealed record BrowserPushSubscription(string Endpoint, string PublicKey, string AuthSecret);

public sealed record BrowserSubscribeResult(string? Outcome, BrowserPushSubscription? Subscription);

public sealed class PushNotificationBrowser(IJSRuntime javaScriptRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/push-notifications.js";

    private IJSObjectReference? _module;

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

    public Task<bool> IsSupportedAsync()
    {
        return InvokeAsync<bool>("readSupport");
    }

    public async Task<PushPermission> ReadPermissionAsync()
    {
        return PushNotificationSection.ParsePermission(await InvokeAsync<string>("readPermission"));
    }

    public async Task<string> ReadDeviceLabelAsync()
    {
        return PushDeviceLabel.Create(await InvokeAsync<string>("readUserAgent"));
    }

    public Task<BrowserPushSubscription?> ReadSubscriptionAsync()
    {
        return InvokeAsync<BrowserPushSubscription?>("readSubscription");
    }

    public Task<string?> ReadSavedSubscriptionIdAsync()
    {
        return InvokeAsync<string?>("readStoredSubscriptionId");
    }

    public Task SaveSubscriptionIdAsync(string? pushSubscriptionId)
    {
        return InvokeAsync<bool>("storeSubscriptionId", pushSubscriptionId);
    }

    public async Task<(PushSubscribeOutcome Outcome, BrowserPushSubscription? Subscription)> SubscribeAsync(string applicationServerKey)
    {
        var result = await InvokeAsync<BrowserSubscribeResult>("subscribe", applicationServerKey);
        if (result is null) return (PushSubscribeOutcome.Failed, null);

        return (PushNotificationSection.ParseOutcome(result.Outcome), result.Subscription);
    }

    public Task<bool> UnsubscribeAsync()
    {
        return InvokeAsync<bool>("unsubscribe");
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
