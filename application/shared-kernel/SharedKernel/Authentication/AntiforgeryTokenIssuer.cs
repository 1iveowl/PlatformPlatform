using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Authentication;

// The port a handler uses to hand a browser its antiforgery pair. Handlers are registered in both the API host and the
// worker host, and antiforgery services exist only in the API host, so the dependency is an abstraction with one
// implementation per host rather than IAntiforgery itself.
public interface IAntiforgeryTokenIssuer
{
    string IssueRequestToken(HttpContext httpContext);
}

// The cookie is written once with the attributes a __Host- cookie requires, and the request token is bound to the
// caller's identity, so a client reads bootstrap again after that changes
public sealed class AntiforgeryTokenIssuer(IAntiforgery antiforgery) : IAntiforgeryTokenIssuer
{
    public string IssueRequestToken(HttpContext httpContext)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        if (tokens.CookieToken is not null)
        {
            httpContext.Response.Cookies.Append(
                AuthenticationTokenHttpKeys.AntiforgeryTokenCookieName,
                tokens.CookieToken,
                new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" }
            );
        }

        return tokens.RequestToken!;
    }
}

// Background workers serve no browser and have no antiforgery services. Registering this keeps the worker host's
// dependency injection valid, and fails loud if a handler that issues tokens is ever invoked outside the API host.
public sealed class UnavailableAntiforgeryTokenIssuer : IAntiforgeryTokenIssuer
{
    public string IssueRequestToken(HttpContext httpContext)
    {
        throw new InvalidOperationException("Antiforgery tokens are issued by the API host only.");
    }
}
