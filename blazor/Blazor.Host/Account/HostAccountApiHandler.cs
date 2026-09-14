// The request-context credential adapter for the typed account API clients the host uses: the static server-rendered form
// handlers and any call made while serving a request.
//
// Call path: direct to ACCOUNT_API_URL, not back through the gateway. The host is a confidential client inside the same
// network as the account API, the way the gateway itself reaches it.
// - Antiforgery: the host validates the posted form token against the __Host-xsrf-token cookie first (UseAntiforgery),
//   then this handler forwards that token as x-xsrf-token with the same cookie, so the account API's AntiforgeryMiddleware
//   runs its own check on the same pair. Both validate because they share the data protection key ring.
// - Tokens to cookies: the account API returns x-refresh-token and x-access-token when a call issues a session, and
//   x-refresh-authentication-tokens-required when a mutation changed the caller's claims; they are copied onto the host
//   response, which leaves through the gateway. The gateway's AuthenticationCookieMiddleware turns the token pair into the
//   session cookies, or refreshes the tokens when asked, and removes all three headers before the browser sees them,
//   exactly as for the React edition's API calls. The token headers are removed from the upstream response once copied, so
//   no typed client result can carry them.
// - Host network: a caller that can reach the account API directly can already do so without this host, with any
//   headers; the account API's own authentication and antiforgery checks are its boundary, not the network. The host
//   adds no credential of its own: it only relays the browser's bearer token, antiforgery pair and client address.
// - Forwarded values: only the client address and scheme the host's forwarded headers middleware accepted are sent,
//   rebuilt from the effective request, so a forged inbound X-Forwarded-For never reaches the account API unchecked.
// - Deployment: the host's container app must be allowed to reach the account API's internal ingress in the same
//   Container Apps environment, as the gateway is.
//
// The HTTP client factory pools this handler across requests and users, so it holds nothing but the context accessor and
// the antiforgery options: every value is read from the current request on each send and set on the request message, and
// the primary handler holds no cookies (UseCookies=false), so nothing crosses between concurrent requests.

using Account.Client;
using Blazor.Host.Shell;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Blazor.Host.Account;

public sealed class HostAccountApiHandler(IHttpContextAccessor httpContextAccessor, IOptions<AntiforgeryOptions> antiforgeryOptions) : DelegatingHandler
{
    private const string RefreshTokenHeaderKey = "x-refresh-token";
    private const string AccessTokenHeaderKey = "x-access-token";
    private const string RefreshAuthenticationTokensHeaderKey = "x-refresh-authentication-tokens-required";

    private static readonly string[] RelayedRequestHeaders = ["User-Agent", "Accept-Language"];

    private static readonly string[] TokenResponseHeaders = [RefreshTokenHeaderKey, AccessTokenHeaderKey];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext.");
        AddRequestCredentials(context, request);

        var response = await base.SendAsync(request, cancellationToken);
        CopyResponseHeaders(response, context);
        return response;
    }

    private void AddRequestCredentials(HttpContext context, HttpRequestMessage request)
    {
        if (context.Request.HasFormContentType)
        {
            var formToken = context.Request.Form[antiforgeryOptions.Value.FormFieldName].ToString();
            if (formToken.Length > 0) request.Headers.TryAddWithoutValidation(HostShell.AntiforgeryHeaderName, formToken);
        }

        if (context.Request.Cookies.TryGetValue(HostShell.AntiforgeryCookieName, out var antiforgeryCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{HostShell.AntiforgeryCookieName}={antiforgeryCookie}");
        }

        // The gateway already replaced the session cookies with a bearer token on the way in
        if (context.Request.Headers.Authorization is { Count: > 0 } authorization)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization.ToString());
        }

        foreach (var header in RelayedRequestHeaders)
        {
            if (context.Request.Headers.TryGetValue(header, out var value)) request.Headers.TryAddWithoutValidation(header, value.ToString());
        }

        request.Headers.TryAddWithoutValidation(AccountApiHeaders.Locale, HostShell.GetLocale(context));

        if (context.Connection.RemoteIpAddress is { } clientAddress)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientAddress.ToString());
        }

        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
    }

    private static void CopyResponseHeaders(HttpResponseMessage response, HttpContext context)
    {
        foreach (var header in TokenResponseHeaders)
        {
            if (!response.Headers.TryGetValues(header, out var values)) continue;

            context.Response.Headers[header] = values.Single();
            response.Headers.Remove(header);
        }

        if (response.Headers.TryGetValues(RefreshAuthenticationTokensHeaderKey, out var refreshRequired))
        {
            context.Response.Headers[RefreshAuthenticationTokensHeaderKey] = refreshRequired.Single();
        }

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                context.Response.Headers.Append("Set-Cookie", setCookie);
            }
        }
    }
}
