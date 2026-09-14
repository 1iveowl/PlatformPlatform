using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Account.Client;
using Blazor.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NUlid;
using SharedKernel.Authentication.TokenSigning;

namespace Blazor.Tests.Account;

public sealed record EmailLoginStartBody(string Email);

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

// Runs the real host on loopback in Development, the way the gateway reaches it, with a stand-in account API that records
// what the host sends. Tokens are signed with the development signing client, which reads the key the AppHost writes.
public sealed partial class HostFixture : IAsyncLifetime
{
    public const string PublicHost = "app.dev.localhost:9000";
    public const string TenantIdClaimValue = "4711";
    public const string FailingEmailPrefix = "fail-";
    public const string FailingFirstName = "fail";
    public const string FieldErrorMessage = "The value is not accepted.";

    private WebApplication? _accountApi;
    private WebApplication? _host;

    public DevelopmentTokenSigningClient TokenSigningClient { get; } = new();

    public ConcurrentDictionary<string, RecordedAccountApiRequest> AccountApiRequests { get; } = new();

    // Shared by all tests; every per-user value is set on the request message, never on the client
    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider HostServices => _host!.Services;

    public async Task InitializeAsync()
    {
        _accountApi = BuildAccountApi();
        await _accountApi.StartAsync();

        Environment.SetEnvironmentVariable("ACCOUNT_API_URL", GetAddress(_accountApi));
        Environment.SetEnvironmentVariable("PUBLIC_URL", $"https://{PublicHost}");
        _host = HostApplication.Build(["--environment", "Development", "--urls", "http://127.0.0.1:0"], TokenSigningClient);
        await _host.StartAsync();
        Client = CreateClient(new Uri(GetAddress(_host)));
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_accountApi is not null) await _accountApi.DisposeAsync();
    }

    private static HttpClient CreateClient(Uri hostUrl)
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = hostUrl };
        // The headers the gateway adds on every proxied request
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", PublicHost);
        return client;
    }

    public string CreateToken(string email, string? issuer = null, string? audience = null, SigningCredentials? signingCredentials = null, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? TokenSigningClient.Issuer,
            Audience = audience ?? TokenSigningClient.Audience,
            Subject = new ClaimsIdentity([new Claim("sub", $"usr_{email}"), new Claim("email", email), new Claim("tenant_id", TenantIdClaimValue), new Claim("tenant_name", $"tenant-of-{email}")]),
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
    public async Task<(string Cookie, string FormToken)> GetLoginFormAsync(HttpClient client, string? bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/login");
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
