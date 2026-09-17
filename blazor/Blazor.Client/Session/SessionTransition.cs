// Logout and tenant switch for any component that offers them, so the header, and later the app shell's user menu, only
// render the state and forward clicks. One transition runs at a time in a runtime: a second click, a logout during a switch
// or a switch during a logout is ignored, and while one is in flight SessionTransitionGate holds back every other
// state-changing account API call.
//
// Logout is complete only on evidence. A 2xx leaves for the login page. A 401 is left to UnauthorizedResponseHandler, whose
// first destination wins. A rejection (400, 403, 5xx) keeps the user signed in and shows a retry state. A lost response or
// a timeout is uncertain, because the account API may have revoked the session and cleared nothing in this browser: it is
// reconciled with one bootstrap read, which leaves for login when the browser has no session any more, lets the handler
// leave on a 401, and otherwise shows the retry state. The logout request itself is never sent again without a new click.
//
// A tenant switch that succeeded has already replaced the session cookies, so the previous identity ends at once: requests
// are refused or discarded, and SessionState clears identity, token, flags, cached pages and toasts. Remembering the tenant
// as the next login's preference is optional and bounded; whether it succeeds, fails or hangs, the authenticated home is
// loaded as a new document. Nothing is rolled back and no work of the previous tenant resumes. A switch whose outcome is
// uncertain is reconciled the same way as a logout: a bootstrap read on another tenant proves the switch and leaves for
// the authenticated home, a 401 is left to the handler, and anything else is presented as the failure it was.

using Account.Client;
using Account.Features.Authentication.Queries;
using Account.Features.Authentication.Requests;
using Blazor.Client.Bootstrap;
using Microsoft.JSInterop;
using SharedKernel.Domain;

namespace Blazor.Client.Session;

public enum SessionTransitionStatus
{
    Idle,
    LoggingOut,
    SwitchingTenant,

    // The account API rejected the logout; the session is still usable here and the user may try again
    LogoutFailed,

    // The logout's outcome could not be established and the browser still holds a usable session; the user may try again
    LogoutUnconfirmed,

    // A full document navigation out of the authenticated surface has been chosen or the session has ended
    Leaving
}

public enum LogoutOutcome
{
    LoggedOut,
    SessionEnded,
    Failed,
    Unconfirmed,
    Ignored
}

public enum TenantSwitchOutcome
{
    Switched,
    SessionEnded,
    Failed,
    Ignored
}

public sealed record TenantSwitchResult(TenantSwitchOutcome Outcome, ApiCallResult? Failure = null);

public sealed class SessionTransition(
    AuthenticationClient authenticationClient,
    AuthenticationNavigator authenticationNavigator,
    SessionTransitionGate gate,
    SessionState session,
    AuthSyncCoordinator authSync,
    IJSRuntime jsRuntime
)
{
    // How long a tenant switch waits for the optional preference cookie before it loads the authenticated home anyway
    public static readonly TimeSpan PreferredTenantTimeout = TimeSpan.FromSeconds(5);

    private SessionTransitionStatus _status;

    public SessionTransitionStatus Status => authenticationNavigator.IsLeaving ? SessionTransitionStatus.Leaving : _status;

    // Logout and tenant switch are unavailable while one is in flight or once the surface is being left
    public bool IsBusy => Status is SessionTransitionStatus.LoggingOut or SessionTransitionStatus.SwitchingTenant or SessionTransitionStatus.Leaving;

    public event Action? Changed;

    public async Task<LogoutOutcome> LogoutAsync()
    {
        var user = session.Current?.User;
        if (!TryBegin(AccountApiRoutes.Logout, SessionTransitionStatus.LoggingOut)) return LogoutOutcome.Ignored;

        try
        {
            var result = await authenticationClient.LogoutAsync(CancellationToken.None);
            switch (result.Outcome)
            {
                case ApiCallOutcome.Success:
                    await LeaveForLoggedOutAsync(user);
                    return LogoutOutcome.LoggedOut;
                case ApiCallOutcome.Unauthorized:
                    return LogoutOutcome.SessionEnded;
                case ApiCallOutcome.TransportFailure:
                    return await ReconcileLogoutAsync(user);
                default:
                    _status = SessionTransitionStatus.LogoutFailed;
                    return LogoutOutcome.Failed;
            }
        }
        finally
        {
            End();
        }
    }

    // The tenant name is only told to the other tabs of this browser, which show it in their reload dialog
    public async Task<TenantSwitchResult> SwitchTenantAsync(TenantId tenantId, string? tenantName = null)
    {
        var previousUser = session.Current?.User;
        if (!TryBegin(AccountApiRoutes.SwitchTenant, SessionTransitionStatus.SwitchingTenant)) return new TenantSwitchResult(TenantSwitchOutcome.Ignored);

        try
        {
            var result = await authenticationClient.SwitchTenantAsync(new SwitchTenantCommand(tenantId), CancellationToken.None);
            switch (result.Outcome)
            {
                case ApiCallOutcome.Success:
                    await LeaveForSwitchedTenantAsync(previousUser, tenantId, tenantName);
                    return new TenantSwitchResult(TenantSwitchOutcome.Switched);
                case ApiCallOutcome.Unauthorized:
                    return new TenantSwitchResult(TenantSwitchOutcome.SessionEnded);
                case ApiCallOutcome.TransportFailure:
                    return await ReconcileSwitchAsync(previousUser, result);
                default:
                    _status = SessionTransitionStatus.Idle;
                    return new TenantSwitchResult(TenantSwitchOutcome.Failed, result);
            }
        }
        finally
        {
            End();
        }
    }

    private bool TryBegin(string transitionPath, SessionTransitionStatus status)
    {
        if (authenticationNavigator.IsLeaving || !gate.TryBegin(transitionPath)) return false;

        _status = status;
        Changed?.Invoke();
        return true;
    }

    private void End()
    {
        gate.End();
        if (_status is SessionTransitionStatus.LoggingOut or SessionTransitionStatus.SwitchingTenant) _status = SessionTransitionStatus.Idle;
        Changed?.Invoke();
    }

    // A safe read, never a second logout: a 401 is handled by the unauthorized handler, an anonymous bootstrap proves the
    // browser holds no session, and an authenticated or failed read leaves the user signed in with the retry state
    private async Task<LogoutOutcome> ReconcileLogoutAsync(BootstrapUser? user)
    {
        var bootstrap = await authenticationClient.GetBootstrapAsync(CancellationToken.None);
        if (bootstrap.Outcome == ApiCallOutcome.Unauthorized) return LogoutOutcome.SessionEnded;

        if (bootstrap.IsSuccess && !bootstrap.Value.IsAuthenticated)
        {
            await LeaveForLoggedOutAsync(user);
            return LogoutOutcome.LoggedOut;
        }

        _status = SessionTransitionStatus.LogoutUnconfirmed;
        return LogoutOutcome.Unconfirmed;
    }

    private async Task<TenantSwitchResult> ReconcileSwitchAsync(BootstrapUser? previousUser, ApiCallResult failure)
    {
        var bootstrap = await authenticationClient.GetBootstrapAsync(CancellationToken.None);
        if (bootstrap.Outcome == ApiCallOutcome.Unauthorized) return new TenantSwitchResult(TenantSwitchOutcome.SessionEnded);

        if (bootstrap.IsSuccess && !bootstrap.Value.IsAuthenticated)
        {
            authenticationNavigator.LeaveForLogin();
            return new TenantSwitchResult(TenantSwitchOutcome.SessionEnded);
        }

        if (bootstrap.IsSuccess && bootstrap.Value.User?.TenantId is { } currentTenantId && currentTenantId != previousUser?.TenantId)
        {
            await LeaveForSwitchedTenantAsync(previousUser, currentTenantId, bootstrap.Value.User.TenantName);
            return new TenantSwitchResult(TenantSwitchOutcome.Switched);
        }

        _status = SessionTransitionStatus.Idle;
        return new TenantSwitchResult(TenantSwitchOutcome.Failed, failure);
    }

    // The other tabs are told after this runtime's session has ended, so none of its work outlives the switch
    private async Task LeaveForSwitchedTenantAsync(BootstrapUser? previousUser, TenantId tenantId, string? tenantName)
    {
        authenticationNavigator.EndSession();
        Changed?.Invoke();
        await authSync.AnnounceAsync(AuthSyncRules.TenantSwitched(previousUser, tenantId, tenantName));
        await TryRememberPreferredTenantAsync(tenantId);
        authenticationNavigator.LeaveForAuthenticatedHome();
    }

    private async Task LeaveForLoggedOutAsync(BootstrapUser? user)
    {
        authenticationNavigator.EndSession();
        await authSync.AnnounceAsync(AuthSyncRules.LoggedOut(user));
        authenticationNavigator.LeaveForLoggedOut();
    }

    // The account API validated the tenant by switching to it; the cookie is only a hint the next login re-validates
    private async Task TryRememberPreferredTenantAsync(TenantId tenantId)
    {
        using var timeout = new CancellationTokenSource(PreferredTenantTimeout);
        try
        {
            await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", timeout.Token, "./js/preferred-tenant.js");
            await module.InvokeVoidAsync("remember", timeout.Token, PreferredTenant.CookieName, PreferredTenant.Format(tenantId), (long)PreferredTenant.MaxAge.TotalSeconds);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
        {
            // The preference is optional: the switch has happened, so the new document is loaded without it
        }
    }
}
