// Server-to-server calls from the static server-rendered form handlers to the account API.
//
// Call path: direct to ACCOUNT_API_URL, not back through the gateway. The host is a confidential client inside the same
// network as the account API, the way the gateway itself reaches it.
// - Antiforgery: the host validates the posted form token against the __Host-xsrf-token cookie first (UseAntiforgery),
//   then this client forwards that token as x-xsrf-token with the same cookie, so the account API's AntiforgeryMiddleware
//   runs its own check on the same pair. Both validate because they share the data protection key ring.
// - Tokens to cookies: the account API returns x-refresh-token and x-access-token on this call; they are copied onto the
//   host response, which leaves through the gateway, and the gateway's AuthenticationCookieMiddleware turns them into the
//   session cookies exactly as for the React edition's API calls.
// - Host network: a caller that can reach the account API directly can already do so without this host, with any
//   headers; the account API's own authentication and antiforgery checks are its boundary, not the network. The host
//   adds no credential of its own: it only relays the browser's bearer token, antiforgery pair and client address.
// - Forwarded values: only the client address and scheme the host's forwarded headers middleware accepted are sent,
//   rebuilt from the effective request, so a forged inbound X-Forwarded-For never reaches the account API unchecked.
// - Deployment: the host's container app must be allowed to reach the account API's internal ingress in the same
//   Container Apps environment, as the gateway is.
//
// Every value is read from the current request and set on the request message; the pooled handler holds no cookies
// (UseCookies=false) and no credentials, so nothing crosses between concurrent requests.

using System.Text.Json;
using Blazor.Host.Shell;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Blazor.Host.Account;

public sealed record AccountApiResult(int StatusCode, JsonElement? Body, string? ErrorMessage)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

public sealed class AccountApiClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor, IOptions<AntiforgeryOptions> antiforgeryOptions)
{
    private const string RefreshTokenHeaderKey = "x-refresh-token";
    private const string AccessTokenHeaderKey = "x-access-token";

    private static readonly string[] RelayedRequestHeaders = ["User-Agent", "Accept-Language"];

    public async Task<AccountApiResult> PostAsync(string path, object body, CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext.");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body);

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

        if (context.Connection.RemoteIpAddress is { } clientAddress)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientAddress.ToString());
        }

        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        foreach (var header in new[] { RefreshTokenHeaderKey, AccessTokenHeaderKey })
        {
            if (response.Headers.TryGetValues(header, out var values)) context.Response.Headers[header] = values.Single();
        }

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                context.Response.Headers.Append("Set-Cookie", setCookie);
            }
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement? json = null;
        if (content.Length > 0 && response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
        {
            json = JsonDocument.Parse(content).RootElement.Clone();
        }

        return new AccountApiResult((int)response.StatusCode, json, response.IsSuccessStatusCode ? null : GetErrorMessage(json, (int)response.StatusCode));
    }

    private static string GetErrorMessage(JsonElement? problem, int statusCode)
    {
        if (problem is not { ValueKind: JsonValueKind.Object } details) return $"Request failed with status {statusCode}.";

        if (details.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            var messages = errors.EnumerateObject().SelectMany(error => error.Value.EnumerateArray().Select(message => message.GetString())).ToArray();
            if (messages.Length > 0) return string.Join(" ", messages);
        }

        if (details.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } detailText) return detailText;
        return details.TryGetProperty("title", out var title) ? title.GetString() ?? "" : $"Request failed with status {statusCode}.";
    }
}
