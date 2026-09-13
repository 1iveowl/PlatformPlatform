// Spike code (Blazor edition, stage B2): server-to-server calls from a static SSR form handler to the account API.
// The browser posted the form through the gateway to this host; this client forwards the posted antiforgery token as
// the x-xsrf-token header with the antiforgery cookie, so the account API enforces its own antiforgery check, and
// copies the authentication token headers and any Set-Cookie back onto this host's response. The gateway then turns
// x-refresh-token and x-access-token into the session cookies exactly as it does for the React edition's API calls.

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

    private static readonly string[] ForwardedRequestHeaders = ["User-Agent", "Accept-Language", "X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host"];

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

        foreach (var header in ForwardedRequestHeaders)
        {
            if (context.Request.Headers.TryGetValue(header, out var value)) request.Headers.TryAddWithoutValidation(header, value.ToString());
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);

        foreach (var header in new[] { RefreshTokenHeaderKey, AccessTokenHeaderKey })
        {
            if (response.Headers.TryGetValues(header, out var values)) context.Response.Headers[header] = values.Single();
        }

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies) context.Response.Headers.Append("Set-Cookie", setCookie);
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
