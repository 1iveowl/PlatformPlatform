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

// What the error page shows, decided once by StatusPageModel.CreateError. Session is set for a /error?error=<code> landing
// with a known code. Otherwise the page shows the failure with Try again (RetryUrl) and Go to home (HomeUrl); Show details
// reveals Message and StackTrace in Development only and the ReferenceId everywhere else. The record is serializable, so a
// server-rendered page can hand it to the WebAssembly shell as a parameter.
public sealed record ErrorPageView(
    StatusPageLayout Layout,
    SessionErrorKind? Session,
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

    // failedPath is the path of the request that threw, including the path base, and failedQuery its query string. A
    // session error always renders in the public layout: the session it reports is gone, so the shell could only leave for
    // login and lose the message. Exception details are revealed in Development only; elsewhere only the reference id.
    public static ErrorPageView CreateError(string? errorCode, bool isAuthenticated, bool isDevelopment, string? failedPath, string? failedQuery, Exception? exception, string? referenceId)
    {
        var session = GetSessionError(errorCode);
        var signedIn = isAuthenticated && session is null;
        var homeUrl = GetHomeUrl(signedIn);
        var showsException = isDevelopment && exception is not null;
        return new ErrorPageView(
            GetLayout(signedIn),
            session,
            homeUrl,
            GetRetryUrl(failedPath, failedQuery, homeUrl),
            showsException ? exception!.Message : null,
            showsException ? exception!.StackTrace : null,
            showsException ? null : referenceId
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
