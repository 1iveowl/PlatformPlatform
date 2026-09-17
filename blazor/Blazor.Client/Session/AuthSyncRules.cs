// Which message from another browser tab, and which server bootstrap, ends the identity this runtime shows. The tabs of one
// browser share the session cookies, so a logout, a login or a tenant switch in one tab replaces the session of every other
// tab. Messages on the auth-sync channel are hints, never identity: they carry no credential, anything in the same origin
// can post them, and a tab never adopts what a message names. A message that proves a change ends the identity at once
// (fail closed); one that names too little to decide asks for a server bootstrap read, which alone can establish the
// identity the browser now holds; one that matches this runtime's identity changes nothing. Messages keep the React
// edition's shape (TENANT_SWITCHED, USER_LOGGED_IN, USER_LOGGED_OUT with a timestamp), so both editions' tabs understand
// each other.

using System.Globalization;
using Account.Features.Authentication.Queries;
using SharedKernel.Domain;

namespace Blazor.Client.Session;

public enum AuthSyncOutcome
{
    // Nothing this runtime shows has changed
    None,

    // The message names too little to decide; a server bootstrap read decides
    Reconcile,

    TenantSwitched,
    DifferentUser,
    LoggedOut
}

public sealed record AuthSyncDecision(AuthSyncOutcome Outcome, string? TenantName = null)
{
    public static readonly AuthSyncDecision None = new(AuthSyncOutcome.None);

    public bool EndsIdentity => Outcome is AuthSyncOutcome.TenantSwitched or AuthSyncOutcome.DifferentUser or AuthSyncOutcome.LoggedOut;
}

// The JSON shape on the channel; a field a message type does not use is null
public sealed record AuthSyncMessage(
    string? Type,
    string? UserId,
    string? TenantId,
    string? NewTenantId,
    string? PreviousTenantId,
    string? TenantName,
    string? Email,
    long Timestamp
);

public sealed record AuthSyncIdentity(bool IsAuthenticated, string? UserId, string? TenantId, string? Email)
{
    public static AuthSyncIdentity From(BootstrapResponse bootstrap)
    {
        return bootstrap is { IsAuthenticated: true, User: { } user }
            ? new AuthSyncIdentity(true, user.Id.Value, FormatTenantId(user.TenantId), user.Email)
            : new AuthSyncIdentity(false, null, null, null);
    }

    public static string FormatTenantId(TenantId tenantId)
    {
        return tenantId.Value.ToString(CultureInfo.InvariantCulture);
    }
}

public static class AuthSyncRules
{
    public const string TenantSwitchedType = "TENANT_SWITCHED";
    public const string UserLoggedInType = "USER_LOGGED_IN";
    public const string UserLoggedOutType = "USER_LOGGED_OUT";

    public static AuthSyncDecision Decide(AuthSyncIdentity current, AuthSyncMessage message)
    {
        // A runtime without an authenticated identity shows nothing another tab could make stale
        if (!current.IsAuthenticated) return AuthSyncDecision.None;

        return message.Type switch
        {
            UserLoggedOutType => new AuthSyncDecision(AuthSyncOutcome.LoggedOut),
            TenantSwitchedType => DecideTenantSwitched(current, message),
            UserLoggedInType => DecideLoggedIn(current, message),
            _ => AuthSyncDecision.None
        };
    }

    // The bootstrap the account API returned for the browser's cookies now, against the identity this runtime started with
    public static AuthSyncDecision Decide(AuthSyncIdentity current, BootstrapResponse server)
    {
        if (!current.IsAuthenticated) return AuthSyncDecision.None;

        var identity = AuthSyncIdentity.From(server);
        if (!identity.IsAuthenticated) return new AuthSyncDecision(AuthSyncOutcome.LoggedOut);
        if (identity.UserId != current.UserId) return new AuthSyncDecision(AuthSyncOutcome.DifferentUser);
        if (identity.TenantId != current.TenantId) return new AuthSyncDecision(AuthSyncOutcome.TenantSwitched, server.User?.TenantName);
        return AuthSyncDecision.None;
    }

    // Announced once per runtime after the first authenticated bootstrap, which is how a login in this tab reaches the others
    public static AuthSyncMessage LoggedIn(BootstrapUser user)
    {
        return new AuthSyncMessage(UserLoggedInType, user.Id.Value, AuthSyncIdentity.FormatTenantId(user.TenantId), null, null, null, user.Email, 0);
    }

    public static AuthSyncMessage TenantSwitched(BootstrapUser? previous, TenantId newTenantId, string? tenantName)
    {
        return new AuthSyncMessage(
            TenantSwitchedType, previous?.Id.Value, null, AuthSyncIdentity.FormatTenantId(newTenantId),
            previous is null ? null : AuthSyncIdentity.FormatTenantId(previous.TenantId), tenantName, null, 0
        );
    }

    public static AuthSyncMessage LoggedOut(BootstrapUser? previous)
    {
        return new AuthSyncMessage(UserLoggedOutType, previous?.Id.Value, null, null, null, null, null, 0);
    }

    private static AuthSyncDecision DecideTenantSwitched(AuthSyncIdentity current, AuthSyncMessage message)
    {
        if (message.UserId is not null && message.UserId != current.UserId) return new AuthSyncDecision(AuthSyncOutcome.DifferentUser);
        if (message.NewTenantId is null) return new AuthSyncDecision(AuthSyncOutcome.Reconcile);
        return message.NewTenantId != current.TenantId ? new AuthSyncDecision(AuthSyncOutcome.TenantSwitched, message.TenantName) : AuthSyncDecision.None;
    }

    private static AuthSyncDecision DecideLoggedIn(AuthSyncIdentity current, AuthSyncMessage message)
    {
        if (message.UserId is not null)
        {
            if (message.UserId != current.UserId) return new AuthSyncDecision(AuthSyncOutcome.DifferentUser);
        }
        else if (message.Email is not null)
        {
            if (!string.Equals(message.Email, current.Email, StringComparison.OrdinalIgnoreCase)) return new AuthSyncDecision(AuthSyncOutcome.DifferentUser);
        }
        else
        {
            return new AuthSyncDecision(AuthSyncOutcome.Reconcile);
        }

        // A login completes with a tenant of its own choosing, which the React edition's login message may leave empty
        if (message.TenantId is null) return new AuthSyncDecision(AuthSyncOutcome.Reconcile);
        return message.TenantId != current.TenantId ? new AuthSyncDecision(AuthSyncOutcome.TenantSwitched) : AuthSyncDecision.None;
    }
}
