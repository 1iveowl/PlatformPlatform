// Keeps this runtime from showing or acting for an identity the browser no longer holds, when another tab of the same browser
// logs out, logs in or switches tenant. wwwroot/js/auth-sync.js wraps BroadcastChannel("auth-sync") and reports its
// messages, and asks for a reconciliation when the document becomes visible, gains focus or is restored from the
// back/forward cache, because a hidden, suspended or restored tab may have missed a message and a browser without
// BroadcastChannel receives none. AuthSyncRules decides; this class acts.
//
// An identity ends through AuthenticationNavigator.EndSession before anything is shown: every request of the runtime is
// refused or discarded, SessionState cancels its reads and clears identity, antiforgery token, flags, cached list pages and
// toasts, and an unsaved-changes guard releases the document, so a dirty form cannot postpone it or submit under the
// replacement session. Only then is Invalidation set, which the shell shows as the non-dismissable dialog whose Reload loads
// the page as a new document; that document's bootstrap is the only way an identity is adopted.
//
// Messages older than or as old as the newest one processed are ignored, so duplicates and out-of-order delivery change
// nothing. A tab announces its identity once per runtime after its first authenticated bootstrap and never in response to a
// message, so tabs cannot echo each other.

using Account.Client;
using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using Microsoft.JSInterop;

namespace Blazor.Client.Session;

public sealed class AuthSyncCoordinator(SessionState session, AuthenticationNavigator authenticationNavigator, IServiceProvider services, IJSRuntime jsRuntime)
    : IAsyncDisposable
{
    public const string ModulePath = "./js/auth-sync.js";

    // How long a transition waits for its message to be posted before it leaves the document anyway
    public static readonly TimeSpan AnnounceTimeout = TimeSpan.FromSeconds(2);

    private IJSObjectReference? _channel;
    private DotNetObjectReference<AuthSyncCoordinator>? _dotNet;
    private AuthSyncIdentity? _identity;
    private bool _isAttached;
    private bool _isReconciling;
    private long _lastTimestamp;
    private IJSObjectReference? _module;

    // Set once this runtime's identity has ended because of another tab; never cleared, because only a new document recovers
    public AuthSyncDecision? Invalidation { get; private set; }

    private bool IsEnded => Invalidation is not null || authenticationNavigator.IsLeaving;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel is not null)
            {
                await _channel.InvokeVoidAsync("dispose");
                await _channel.DisposeAsync();
            }

            if (_module is not null) await _module.DisposeAsync();
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
            // The document is gone, and its channel and listeners with it
        }

        _dotNet?.Dispose();
    }

    public event Action? Changed;

    // Starts listening for the identity of the first authenticated bootstrap; later calls, and an anonymous bootstrap, do nothing
    public async Task AttachAsync(BootstrapResponse bootstrap)
    {
        if (_isAttached || !bootstrap.IsAuthenticated || bootstrap.User is null) return;

        _isAttached = true;
        _identity = AuthSyncIdentity.From(bootstrap);
        try
        {
            _dotNet = DotNetObjectReference.Create(this);
            _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _channel = await _module.InvokeAsync<IJSObjectReference>("attach", _dotNet, AuthSyncRules.LoggedIn(bootstrap.User));
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // Without the module the runtime still ends on the next 401; nothing else depends on it
        }
    }

    // Posts a transition of this tab for the others; best effort and bounded, because the transition leaves the document next
    public async Task AnnounceAsync(AuthSyncMessage message)
    {
        if (_channel is null) return;

        using var timeout = new CancellationTokenSource(AnnounceTimeout);
        try
        {
            await _channel.InvokeVoidAsync("post", timeout.Token, message);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
        {
            // The other tabs reconcile when they are next shown or focused
        }
    }

    [JSInvokable]
    public async Task OnMessage(AuthSyncMessage message)
    {
        if (_identity is null || IsEnded || message.Timestamp <= _lastTimestamp) return;

        _lastTimestamp = message.Timestamp;
        var decision = AuthSyncRules.Decide(_identity, message);
        if (decision.Outcome == AuthSyncOutcome.Reconcile)
        {
            await Reconcile();
            return;
        }

        if (decision.EndsIdentity) EndIdentity(decision);
    }

    // A safe bootstrap read compared with this runtime's identity. It is never committed to SessionState: a different
    // identity ends this one, and a 401 is left to the unauthorized handler, which leaves for the page its reason names.
    [JSInvokable]
    public async Task Reconcile()
    {
        if (_identity is null || IsEnded || _isReconciling) return;

        _isReconciling = true;
        try
        {
            var result = await session.UnlessLeavingAsync(services.GetRequiredService<AuthenticationClient>().GetBootstrapAsync);
            if (result is not { IsSuccess: true } || IsEnded) return;

            var decision = AuthSyncRules.Decide(_identity, result.Value);
            if (decision.EndsIdentity) EndIdentity(decision);
        }
        finally
        {
            _isReconciling = false;
        }
    }

    public void Reload()
    {
        authenticationNavigator.LeaveForReload();
    }

    private void EndIdentity(AuthSyncDecision decision)
    {
        authenticationNavigator.EndSession();
        Invalidation = decision;
        Changed?.Invoke();
    }
}
