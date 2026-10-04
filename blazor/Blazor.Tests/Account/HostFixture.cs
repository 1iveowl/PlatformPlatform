using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Account.Client;
using Blazor.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NUlid;
using SharedKernel.Authentication.BackOfficeIdentity;
using SharedKernel.Authentication.TokenSigning;

namespace Blazor.Tests.Account;

public sealed record EmailLoginStartBody(string Email);

public sealed record CompleteEmailLoginBody(string OneTimePassword);

public sealed record UpdateCurrentUserBody(string FirstName, string LastName, string Title);

public sealed record RecordedAccountApiRequest(
    string? Authorization,
    string? Cookie,
    string? AntiforgeryToken,
    string? ForwardedFor,
    string? ForwardedProto,
    string? ForwardedHost,
    string? Locale
);

// One host for every test class that needs it: InitializeAsync sets ACCOUNT_API_URL and PUBLIC_URL for the process, so a
// second fixture running in parallel would point the first host at another stand-in account API
[CollectionDefinition(Name)]
public sealed class HostCollection : ICollectionFixture<HostFixture>
{
    public const string Name = "Host";
}

// Runs the real host on loopback in Development, the way the gateway reaches it, with a stand-in account API that records
// what the host sends. Tokens are signed with the development signing client, which reads the key the AppHost writes.
public sealed partial class HostFixture : IAsyncLifetime
{
    public const string PublicHost = "app.dev.localhost:9000";
    public const string BackOfficeHost = "back-office.dev.localhost:9001";
    public const string BackOfficeAdminsGroupId = "BackOfficeAdmins";
    public const string TenantIdClaimValue = "4711";
    public const string FailingEmailPrefix = "fail-";
    public const string FailingFirstName = "fail";
    public const string FieldErrorMessage = "The value is not accepted.";
    public const string LockedOneTimePassword = "LOCKED";
    public const string WrongCodeMessage = "The code is wrong or no longer valid.";
    public const string TooManyAttemptsMessage = "Too many attempts, please request a new code.";

    private WebApplication? _accountApi;
    private WebApplication? _host;

    public DevelopmentTokenSigningClient TokenSigningClient { get; } = new();

    public ConcurrentDictionary<string, RecordedAccountApiRequest> AccountApiRequests { get; } = new();

    // The one-time password each email login completion received, by email login id
    public ConcurrentDictionary<string, string> CompletedOneTimePasswords { get; } = new();

    // Shared by all tests; every per-user value is set on the request message, never on the client
    public HttpClient Client { get; private set; } = null!;

    // Without the gateway's forwarded headers, for a caller that reaches the host's port directly
    public HttpClient DirectClient { get; private set; } = null!;

    public IServiceProvider HostServices => _host!.Services;

    public async Task InitializeAsync()
    {
        _accountApi = BuildAccountApi();
        await _accountApi.StartAsync();

        Environment.SetEnvironmentVariable("ACCOUNT_API_URL", GetAddress(_accountApi));
        Environment.SetEnvironmentVariable("PUBLIC_URL", $"https://{PublicHost}");
        Environment.SetEnvironmentVariable("BACK_OFFICE_PUBLIC_URL", $"https://{BackOfficeHost}");
        Environment.SetEnvironmentVariable("BACK_OFFICE_SUBSCRIPTION_ENABLED", "true");
        _host = HostApplication.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0"], TokenSigningClient);
        await _host.StartAsync();
        Client = CreateClient(new Uri(GetAddress(_host)));
        DirectClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(GetAddress(_host)) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        DirectClient.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_accountApi is not null) await _accountApi.DisposeAsync();
    }

    // A second host built the way the first is, with extra command-line configuration that overrides the process-wide
    // variables, for a test of a deployment setting; the caller disposes it. Tests of this collection never run in parallel,
    // so the variables the first host was built with still hold.
    public Task<AdditionalHost> StartAdditionalHostAsync(params string[] configuration)
    {
        return StartAdditionalHostAsync(null, configuration);
    }

    // The same, with the clock the host and its prerendered components read the current time from
    public async Task<AdditionalHost> StartAdditionalHostAsync(TimeProvider? timeProvider, params string[] configuration)
    {
        var host = HostApplication.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0", ..configuration], TokenSigningClient, timeProvider);
        await host.StartAsync();
        return new AdditionalHost(host, CreateClient(new Uri(GetAddress(host))));
    }

    private static HttpClient CreateClient(Uri hostUrl)
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = hostUrl };
        // The headers the gateway adds on every proxied request
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", PublicHost);
        return client;
    }

    // The request the account API's back-office listener sends when it forwards a back-office page: the back-office host in
    // X-Forwarded-Host and, for a signed-in identity, the protected identity it authenticated
    public static HttpRequestMessage CreateBackOfficeRequest(string path, string? protectedIdentity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-Host", BackOfficeHost);
        request.Headers.Accept.ParseAdd("text/html");
        if (protectedIdentity is not null) request.Headers.Add(ForwardedBackOfficeIdentity.HeaderName, protectedIdentity);
        return request;
    }

    // The identity the account API's back-office handler builds from the mock's principal headers, protected the way its
    // back-office listener protects it, with the host's own key ring unless another provider is given
    public string ProtectBackOfficeIdentity(string name, bool isAdmin, IDataProtectionProvider? dataProtectionProvider = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, $"oid-{name}"), new Claim(ClaimTypes.Email, $"{name}@dev.localhost"),
                    ..isAdmin ? [new Claim("groups", BackOfficeAdminsGroupId)] : Array.Empty<Claim>()
                ], "BackOfficeIdentity"
            )
        );
        return ForwardedBackOfficeIdentity.Protect(dataProtectionProvider ?? HostServices.GetRequiredService<IDataProtectionProvider>(), principal, isAdmin);
    }

    // The principal headers the platform authentication injects, as a caller that bypasses it would forge them
    public static void AddPrincipalHeaders(HttpRequestMessage request, string name)
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                $$"""{"auth_typ":"aad","claims":[{"typ":"name","val":"{{name}}"},{"typ":"groups","val":"{{BackOfficeAdminsGroupId}}"}]}"""
            )
        );
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL-NAME", name);
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", $"oid-{name}");
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", payload);
    }

    public string CreateToken(string email, string? issuer = null, string? audience = null, SigningCredentials? signingCredentials = null, DateTime? expires = null, string? locale = null, string? firstName = "Ann")
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? TokenSigningClient.Issuer,
            Audience = audience ?? TokenSigningClient.Audience,
            Subject = new ClaimsIdentity([new Claim("sub", $"usr_{email}"), new Claim("email", email), new Claim("tenant_id", TenantIdClaimValue), new Claim("tenant_name", $"tenant-of-{email}"), ..firstName is null ? Array.Empty<Claim>() : [new Claim("given_name", firstName)]]),
            Claims = locale is null ? null : new Dictionary<string, object> { ["locale"] = locale },
            IssuedAt = (expires ?? now).AddMinutes(-10),
            NotBefore = (expires ?? now).AddMinutes(-10),
            Expires = expires ?? now.AddMinutes(5),
            SigningCredentials = signingCredentials ?? TokenSigningClient.GetSigningCredentials()
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static SigningCredentials CreateForeignSigningCredentials()
    {
        return new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)), SecurityAlgorithms.HmacSha512);
    }

    // Reads the login page as the given user and returns the antiforgery cookie and the form token issued with it
    public Task<(string Cookie, string FormToken)> GetLoginFormAsync(HttpClient client, string? bearerToken)
    {
        return GetFormAsync(client, "blazor/login", bearerToken);
    }

    // Reads a page with a static form and returns the antiforgery cookie and the form token issued with it
    public async Task<(string Cookie, string FormToken)> GetFormAsync(HttpClient client, string path, string? bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearerToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-xsrf-token=", StringComparison.Ordinal)).Split(';')[0];
        var formToken = FormTokenPattern().Match(await response.Content.ReadAsStringAsync()).Groups[1].Value;
        return (cookie, formToken);
    }

    public static HttpRequestMessage CreateLoginPost(string email, string cookie, string formToken, string? bearerToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "blazor/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["_handler"] = "login-start",
                    ["__RequestVerificationToken"] = formToken,
                    ["Input.Email"] = email
                }
            )
        };
        request.Headers.Add("Cookie", cookie);
        if (bearerToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }

    private WebApplication BuildAccountApi()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var accountApi = builder.Build();
        accountApi.MapPost(AccountApiRoutes.StartEmailLogin, async (HttpContext context, EmailLoginStartBody body) =>
            {
                AccountApiRequests[body.Email] = RecordRequest(context);

                // Holds the call open so concurrent form posts overlap inside the host
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                if (body.Email.StartsWith(FailingEmailPrefix, StringComparison.Ordinal)) return CreateValidationProblem("email");

                context.Response.Headers["x-access-token"] = $"access-{body.Email}";
                context.Response.Headers["x-refresh-token"] = $"refresh-{body.Email}";
                return Results.Json(new { emailLoginId = $"emlog_{Ulid.NewUlid()}", validForSeconds = 300 });
            }
        );

        // A signup start and the two code resends, each granting a code valid for 300 seconds, so the verification state a
        // handler writes into the redirect is observable
        accountApi.MapPost(AccountApiRoutes.StartEmailSignup, () => Results.Json(new { emailLoginId = $"emlog_{Ulid.NewUlid()}", validForSeconds = 300 }));
        accountApi.MapPost("/api/account/authentication/email/login/{emailLoginId}/resend-code", () => Results.Json(new { validForSeconds = 300 }));
        accountApi.MapPost("/api/account/authentication/email/signup/{emailLoginId}/resend-code", () => Results.Json(new { validForSeconds = 300 }));

        // A login completion that is always refused, the way the account API refuses a wrong code (400) or a fourth attempt
        // (403), so the verification page's states are observable without a real code
        accountApi.MapPost("/api/account/authentication/email/login/{emailLoginId}/complete", (string emailLoginId, CompleteEmailLoginBody body) =>
            {
                CompletedOneTimePasswords[emailLoginId] = body.OneTimePassword;
                return body.OneTimePassword == LockedOneTimePassword
                    ? Results.Problem(TooManyAttemptsMessage, statusCode: StatusCodes.Status403Forbidden)
                    : Results.Problem(WrongCodeMessage, statusCode: StatusCodes.Status400BadRequest);
            }
        );

        // A profile change: on success the account API asks the gateway to refresh the caller's tokens, and this stand-in
        // also returns a token pair and a cookie so their relay is observable; a failure returns neither
        accountApi.MapPut(AccountApiRoutes.CurrentUser, async (HttpContext context, UpdateCurrentUserBody body) =>
            {
                AccountApiRequests[body.LastName] = RecordRequest(context);

                await Task.Delay(TimeSpan.FromMilliseconds(200));
                if (body.FirstName == FailingFirstName) return CreateValidationProblem("firstName");

                context.Response.Headers["x-refresh-authentication-tokens-required"] = "true";
                context.Response.Headers["x-access-token"] = $"access-{body.LastName}";
                context.Response.Headers["x-refresh-token"] = $"refresh-{body.LastName}";
                context.Response.Headers.Append("Set-Cookie", $"stand-in={body.LastName}; path=/");
                return Results.NoContent();
            }
        );
        return accountApi;
    }

    private static RecordedAccountApiRequest RecordRequest(HttpContext context)
    {
        var headers = context.Request.Headers;
        return new RecordedAccountApiRequest(
            headers.Authorization.FirstOrDefault(), headers.Cookie.FirstOrDefault(), headers["x-xsrf-token"].FirstOrDefault(),
            headers["X-Forwarded-For"].FirstOrDefault(), headers["X-Forwarded-Proto"].FirstOrDefault(), headers["X-Forwarded-Host"].FirstOrDefault(),
            headers["X-Locale"].FirstOrDefault()
        );
    }

    private static IResult CreateValidationProblem(string fieldKey)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { [fieldKey] = [FieldErrorMessage] }, title: "One or more validation errors occurred.");
    }

    private static string GetAddress(WebApplication application)
    {
        return application.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex FormTokenPattern();
}

public sealed record AdditionalHost(WebApplication Host, HttpClient Client) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
    }
}
