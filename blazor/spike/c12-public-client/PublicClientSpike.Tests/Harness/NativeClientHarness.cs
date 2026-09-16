using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Account.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using PublicClientSpike.Tests.Adapter;
using SharedKernel.Authentication;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PublicClientSpike.Tests.Harness;

// SPIKE CODE (T012). The account host with antiforgery validation active and the candidate authorization server added.
public sealed class PublicClientSpikeFactory : AccountWebApplicationFactory
{
    public PublicClientSpikeFactory()
    {
        Environment.SetEnvironmentVariable("BypassAntiforgeryValidation", "false");
    }

    public SpikeClock Clock { get; } = new();

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        services.AddPublicClientAuthorizationServer(Clock);
    }
}

public sealed record Pkce(string Verifier, string Challenge)
{
    public static Pkce Create()
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return new Pkce(verifier, WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }
}

public sealed record NativeTokens(string AccessToken, string RefreshToken, string? SessionId);

// A native client that holds no cookie jar: every request is built from the tokens it received, and the cookie header is set
// only where the account API demands the antiforgery pair from bootstrap.
public sealed class NativeClientHarness(AccountWebApplicationFactory factory)
{
    public const string DesktopClientId = "native-desktop";
    public const string MobileClientId = "native-mobile";
    public const string DesktopRedirectUri = "http://127.0.0.1:53817/callback";
    public const string MobileRedirectUri = "dk.platformplatform.spike:/oauth2redirect";
    public const string Scope = "account.api";

    public HttpClient CreateClient(string? accessToken = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        if (accessToken is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    // The web session is represented by the bearer token the gateway derives from the browser's session cookie
    public async Task<HttpResponseMessage> AuthorizeAsync(string webAccessToken, Dictionary<string, string?> parameters, string fetchMode = "navigate")
    {
        using var client = CreateClient(webAccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString(PublicClientAuthorizationServer.AuthorizePath, parameters));
        request.Headers.Add("Sec-Fetch-Mode", fetchMode);
        request.Headers.Add("Sec-Fetch-Dest", "document");
        return await client.SendAsync(request);
    }

    public static Dictionary<string, string?> AuthorizeParameters(Pkce pkce, string clientId = DesktopClientId, string redirectUri = DesktopRedirectUri)
    {
        return new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scope,
            ["state"] = Guid.NewGuid().ToString("N"),
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256"
        };
    }

    public async Task<string> GetCodeAsync(string webAccessToken, Pkce pkce, string clientId = DesktopClientId, string redirectUri = DesktopRedirectUri)
    {
        var parameters = AuthorizeParameters(pkce, clientId, redirectUri);
        var response = await AuthorizeAsync(webAccessToken, parameters);
        var location = response.Headers.Location ?? throw new InvalidOperationException($"Authorize returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var query = QueryHelpers.ParseQuery(new Uri(location.ToString()).Query);
        if (location.ToString().Split('?')[0] != redirectUri || query["state"] != parameters["state"]) throw new InvalidOperationException($"Unexpected redirect '{location}'.");
        return query["code"].ToString();
    }

    public async Task<HttpResponseMessage> ExchangeAsync(string code, string verifier, string clientId = DesktopClientId, string redirectUri = DesktopRedirectUri, string? clientSecret = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = verifier, ["client_id"] = clientId, ["redirect_uri"] = redirectUri
        };
        if (clientSecret is not null) form["client_secret"] = clientSecret;
        using var client = CreateClient();
        return await client.PostAsync(PublicClientAuthorizationServer.TokenPath, new FormUrlEncodedContent(form));
    }

    public async Task<HttpResponseMessage> RefreshAsync(string refreshToken, string clientId = DesktopClientId)
    {
        using var client = CreateClient();
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = clientId };
        return await client.PostAsync(PublicClientAuthorizationServer.TokenPath, new FormUrlEncodedContent(form));
    }

    public async Task<NativeTokens> SignInAsync(string webAccessToken, string clientId = DesktopClientId, string redirectUri = DesktopRedirectUri)
    {
        var pkce = Pkce.Create();
        var code = await GetCodeAsync(webAccessToken, pkce, clientId, redirectUri);
        return await ReadTokensAsync(await ExchangeAsync(code, pkce.Verifier, clientId, redirectUri));
    }

    public async Task<NativeTokens> RefreshTokensAsync(string refreshToken, string clientId = DesktopClientId)
    {
        return await ReadTokensAsync(await RefreshAsync(refreshToken, clientId));
    }

    public static async Task<NativeTokens> ReadTokensAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Token endpoint returned {(int)response.StatusCode}: {body}");
        return new NativeTokens(
            body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!,
            body.TryGetProperty("session_id", out var sessionId) ? sessionId.GetString() : null
        );
    }

    // A cookie-free write: the bearer token plus the antiforgery pair bootstrap issued to this identity, sent explicitly
    public async Task<HttpResponseMessage> WriteAsync(string accessToken, HttpMethod method, string url, object? body = null)
    {
        using var client = CreateClient(accessToken);
        var bootstrap = await client.GetAsync("/api/account/bootstrap");
        var antiforgeryCookie = bootstrap.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{AuthenticationTokenHttpKeys.AntiforgeryTokenCookieName}=", StringComparison.Ordinal))
            .Split(';')[0];
        var requestToken = (await ReadJsonAsync(bootstrap)).GetProperty("antiforgeryToken").GetString()!;
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Cookie", antiforgeryCookie);
        request.Headers.Add(AuthenticationTokenHttpKeys.AntiforgeryTokenHttpHeaderKey, requestToken);
        return await client.SendAsync(request);
    }

    // OpenIddict renders authorization endpoint errors as plain text lines; the token endpoint answers in JSON
    public static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (content.StartsWith('{')) return JsonDocument.Parse(content).RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        return content.Split('\n').FirstOrDefault(line => line.StartsWith("error:", StringComparison.Ordinal))?["error:".Length..];
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return content.Length == 0 ? default : JsonDocument.Parse(content).RootElement.Clone();
    }
}
