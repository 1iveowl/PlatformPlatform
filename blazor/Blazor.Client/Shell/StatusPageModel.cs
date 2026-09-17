namespace Blazor.Client.Shell;

public enum StatusPageLayout
{
    // The public navigation, for a visitor without a session and for a session that has ended
    Public,

    // The authenticated shell with the sidebar, for a signed-in user
    Shell
}

public enum SessionErrorKind
{
    Revoked,
    Expired,
    TenantDeleted
}

// The refusals an external login or signup callback redirects to, with the React edition's error codes. The MitID
// verification refusals (identity_already_linked, assurance_level_insufficient) are not login or signup outcomes.
public enum AuthenticationErrorKind
{
    UserNotFound,
    AccountAlreadyExists,
    EmailNotProvided,
    AuthenticationFailed,
    InvalidRequest,
    AccessDenied,
    IdentityNotVerified,
    ServerError
}

// Where an action on the error page leads
public enum ErrorPageAction
{
    LogIn,
    SignUp
}

// What the error page shows, decided once by StatusPageModel.CreateError. Session or Authentication is set for a
// /error?error=<code> landing with a known code, ErrorCode then holds that code and Actions the links the page offers.
// Otherwise the page shows the failure with Try again (RetryUrl) and Go to home (HomeUrl); Show details
// reveals Message and StackTrace in Development only and the ReferenceId everywhere else. The record is serializable, so a
// server-rendered page can hand it to the WebAssembly shell as a parameter.
public sealed record ErrorPageView(
    StatusPageLayout Layout,
    SessionErrorKind? Session,
    AuthenticationErrorKind? Authentication,
    string? ErrorCode,
    ErrorPageAction[] Actions,
    string HomeUrl,
    string RetryUrl,
    string? Message,
    string? StackTrace,
    string? ReferenceId
);

// The decisions behind the not-found and error pages, which the host renders for every unknown route and for an unhandled
// exception: which layout they render in, where Go to home and Try again lead, and which details the error page may reveal.
public static class StatusPageModel
{
    public const string SessionRevokedCode = "session_revoked";
    public const string SessionNotFoundCode = "session_not_found";
    public const string SessionExpiredCode = "session_expired";
    public const string TenantDeletedCode = "tenant_deleted";
    public const string UserNotFoundCode = "user_not_found";
    public const string AccountAlreadyExistsCode = "account_already_exists";
    public const string EmailNotProvidedCode = "email_not_provided";
    public const string AuthenticationFailedCode = "authentication_failed";
    public const string InvalidRequestCode = "invalid_request";
    public const string AccessDeniedCode = "access_denied";
    public const string IdentityNotVerifiedCode = "identity_not_verified";
    public const string ServerErrorCode = "server_error";

    // An external login id is a prefix, an underscore and a ULID; anything else is not a reference support can look up
    private const int MaximumReferenceIdLength = 64;

    public static StatusPageLayout GetLayout(bool isAuthenticated)
    {
        return isAuthenticated ? StatusPageLayout.Shell : StatusPageLayout.Public;
    }

    public static string GetHomeUrl(bool isAuthenticated)
    {
        return isAuthenticated ? AppUrls.AuthenticatedHome : AppUrls.ToAbsolute("");
    }

    public static SessionErrorKind? GetSessionError(string? errorCode)
    {
        return errorCode switch
        {
            SessionRevokedCode => SessionErrorKind.Revoked,
            SessionNotFoundCode or SessionExpiredCode => SessionErrorKind.Expired,
            TenantDeletedCode => SessionErrorKind.TenantDeleted,
            _ => null
        };
    }

    public static AuthenticationErrorKind? GetAuthenticationError(string? errorCode)
    {
        return errorCode switch
        {
            UserNotFoundCode => AuthenticationErrorKind.UserNotFound,
            AccountAlreadyExistsCode => AuthenticationErrorKind.AccountAlreadyExists,
            EmailNotProvidedCode => AuthenticationErrorKind.EmailNotProvided,
            AuthenticationFailedCode => AuthenticationErrorKind.AuthenticationFailed,
            InvalidRequestCode => AuthenticationErrorKind.InvalidRequest,
            AccessDeniedCode => AuthenticationErrorKind.AccessDenied,
            IdentityNotVerifiedCode => AuthenticationErrorKind.IdentityNotVerified,
            ServerErrorCode => AuthenticationErrorKind.ServerError,
            _ => null
        };
    }

    // The primary action first, as in the React edition: no account leads to signup, an existing account to login, and a
    // provider without an email to signup with email; every other refusal is retried from login
    public static ErrorPageAction[] GetActions(AuthenticationErrorKind authentication)
    {
        return authentication switch
        {
            AuthenticationErrorKind.UserNotFound or AuthenticationErrorKind.EmailNotProvided => [ErrorPageAction.SignUp, ErrorPageAction.LogIn],
            AuthenticationErrorKind.AccountAlreadyExists => [ErrorPageAction.LogIn, ErrorPageAction.SignUp],
            _ => [ErrorPageAction.LogIn]
        };
    }

    public static string GetActionUrl(ErrorPageAction action)
    {
        return AppUrls.ToAbsolute(action == ErrorPageAction.SignUp ? "signup" : "login");
    }

    // The reference id of a refused external login as the query carried it, when it has the shape of one; otherwise none
    public static string? GetReferenceId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaximumReferenceIdLength) return null;

        return id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ? id : null;
    }

    // failedPath is the path of the request that threw, including the path base, and failedQuery its query string. A
    // session error always renders in the public layout: the session it reports is gone, so the shell could only leave for
    // login and lose the message. Exception details are revealed in Development only; elsewhere only the reference id.
    // A known code always renders in the public layout too: a session error because the session is gone, and a login or
    // signup refusal because it happened without a session. For a known code the reference id is the one the query
    // carried (errorId), shown only when it has the shape of an external login id; the code itself is never echoed
    // unless it is one of the known ones.
    public static ErrorPageView CreateError(string? errorCode, string? errorId, bool isAuthenticated, bool isDevelopment, string? failedPath, string? failedQuery, Exception? exception, string? referenceId)
    {
        var session = GetSessionError(errorCode);
        var authentication = session is null ? GetAuthenticationError(errorCode) : null;
        var knownCode = session is not null || authentication is not null;
        var signedIn = isAuthenticated && !knownCode;
        var homeUrl = GetHomeUrl(signedIn);
        var showsException = isDevelopment && exception is not null;
        return new ErrorPageView(
            GetLayout(signedIn),
            session,
            authentication,
            knownCode ? errorCode : null,
            authentication is { } kind ? GetActions(kind) : [],
            homeUrl,
            GetRetryUrl(failedPath, failedQuery, homeUrl),
            showsException ? exception!.Message : null,
            showsException ? exception!.StackTrace : null,
            knownCode ? GetReferenceId(errorId) : showsException ? null : referenceId
        );
    }

    // Try again returns to the page that failed when it is a local path below the path base, and to home otherwise
    public static string GetRetryUrl(string? failedPath, string? failedQuery, string homeUrl)
    {
        if (string.IsNullOrEmpty(failedPath)) return homeUrl;

        var url = $"{failedPath}{failedQuery}";
        return AppUrls.SanitizeReturnPath(url) == url ? url : homeUrl;
    }
}
