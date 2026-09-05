using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Account.Database;
using Account.Features.ExternalAuthentication;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using Account.Integrations.OAuth;
using Bogus;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Authentication;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Domain;
using SharedKernel.EntityFramework;
using SharedKernel.ExecutionContext;
using SharedKernel.Integrations.Email;
using SharedKernel.SinglePageApp;
using SharedKernel.Telemetry;
using SharedKernel.Tests.Persistence;
using SharedKernel.Tests.Telemetry;
using ExternalIdentity = Account.Features.ExternalAuthentication.Domain.ExternalIdentity;

namespace Account.Tests.ExternalAuthentication;

public abstract class ExternalAuthenticationTestBase : IDisposable
{
    // Tests use the in-memory test server (WebApplicationFactory); no real listener is bound.
    // SinglePageAppConfiguration only consumes this as a URI.
    protected const string PublicUrl = "https://localhost";

    // The verification evidence seeded by InsertVerifiedMitIdIdentity. Fixed values so a test can assert that a
    // login left every one of them untouched, which is what proves a login does not refresh a verification.
    protected static readonly DateTimeOffset VerifiedAt = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    protected static readonly DateTimeOffset AuthenticatedAt = new(2026, 9, 1, 9, 59, 0, TimeSpan.Zero);
    protected static readonly ExternalLoginId VerifiedByExternalLoginId = ExternalLoginId.NewId();

    protected readonly Faker Faker = new();
    protected readonly TelemetryEventsCollectorSpy TelemetryEventsCollectorSpy;
    protected readonly TimeProvider TimeProvider;
    private readonly WebApplicationFactory<Program> _webApplicationFactory;

    protected ExternalAuthenticationTestBase()
    {
        Environment.SetEnvironmentVariable(SinglePageAppConfiguration.PublicUrlKey, PublicUrl);
        Environment.SetEnvironmentVariable(SinglePageAppConfiguration.CdnUrlKey, $"{PublicUrl}/account");
        Environment.SetEnvironmentVariable(
            "APPLICATIONINSIGHTS_CONNECTION_STRING",
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost;LiveEndpoint=https://localhost"
        );
        Environment.SetEnvironmentVariable("BypassAntiforgeryValidation", "true");

        TimeProvider = TimeProvider.System;

        Connection = new SqliteConnection($"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        Connection.Open();

        using (var command = Connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA recursive_triggers = ON;";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA ignore_check_constraints = OFF;";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA trusted_schema = OFF;";
            command.ExecuteNonQuery();
        }

        // Fill this test's database from the seeded template with a fast binary copy instead of
        // recreating the schema and reseeding per test. The shared seeder's entity references match the
        // rows copied into this connection.
        DatabaseSeeder = SeededDatabaseTemplate.EnsureSeeded();
        SeededDatabaseTemplate.RestoreInto(Connection);

        TelemetryEventsCollectorSpy = new TelemetryEventsCollectorSpy(new TelemetryEventsCollector());
        var emailClient = Substitute.For<IEmailClient>();

        _webApplicationFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => { logging.AddFilter(_ => false); });

                builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["OAuth:AllowMockProvider"] = "true",
                                // Both MitID purposes are on for the suite. A test that needs one of them off
                                // overrides the value in its own host rather than relying on the key being absent.
                                ["OAuth:MitId:VerificationEnabled"] = "true",
                                ["OAuth:MitId:LoginEnabled"] = "true"
                            }
                        );
                    }
                );

                builder.ConfigureTestServices(testServices =>
                    {
                        testServices.Remove(testServices.Single(d => d.ServiceType == typeof(IDbContextOptionsConfiguration<AccountDbContext>)));
                        testServices.AddDbContext<AccountDbContext>(options => { options.UseSqlite(Connection).UseSnakeCaseNamingConvention(); });

                        testServices.AddScoped<ITelemetryEventsCollector>(_ => TelemetryEventsCollectorSpy);

                        testServices.Remove(testServices.Single(d => d.ServiceType == typeof(IEmailClient)));
                        testServices.AddTransient<IEmailClient>(_ => emailClient);

                        testServices.AddScoped<IExecutionContext, HttpExecutionContext>();
                    }
                );
            }
        );

        NoRedirectHttpClient = CreateBrowserLikeClient();

        using var serviceScope = _webApplicationFactory.Services.CreateScope();
        var accessTokenGenerator = serviceScope.ServiceProvider.GetRequiredService<AccessTokenGenerator>();

        AuthenticatedOwnerHttpClient = CreateBrowserLikeClient();
        AuthenticatedOwnerHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", accessTokenGenerator.Generate(CreateUserInfo(DatabaseSeeder.Tenant1Owner, DatabaseSeeder.Tenant1OwnerSession.Id))
        );

        AuthenticatedMemberHttpClient = CreateBrowserLikeClient();
        AuthenticatedMemberHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", accessTokenGenerator.Generate(CreateUserInfo(DatabaseSeeder.Tenant1Member, DatabaseSeeder.Tenant1MemberSession.Id))
        );
    }

    protected SqliteConnection Connection { get; }

    protected DatabaseSeeder DatabaseSeeder { get; }

    protected HttpClient NoRedirectHttpClient { get; }

    protected HttpClient AuthenticatedOwnerHttpClient { get; }

    protected HttpClient AuthenticatedMemberHttpClient { get; }

    protected IServiceProvider WebApplicationServices => _webApplicationFactory.Services;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected async Task<(string CallbackUrl, string[] Cookies)> StartLoginFlow(string? returnPath = null, string? locale = null, TenantId? preferredTenantId = null, ExternalProviderType providerType = ExternalProviderType.Google)
    {
        var url = BuildStartUrl(providerType, "login", returnPath, locale, preferredTenantId);
        var response = await NoRedirectHttpClient.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return (response.Headers.Location!.ToString(), ExtractSetCookieHeaders(response));
    }

    protected async Task<(string CallbackUrl, string[] Cookies)> StartSignupFlow(string? returnPath = null, string? locale = null, ExternalProviderType providerType = ExternalProviderType.Google)
    {
        var url = BuildStartUrl(providerType, "signup", returnPath, locale);
        var response = await NoRedirectHttpClient.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return (response.Headers.Location!.ToString(), ExtractSetCookieHeaders(response));
    }

    /// <summary>
    ///     Starts a verification flow as the given signed-in user and returns the callback URL the mock provider would
    ///     redirect the browser to, together with the cookies to replay into the callback.
    /// </summary>
    protected async Task<(string CallbackUrl, string[] Cookies)> StartVerificationFlow(HttpClient authenticatedHttpClient, ExternalProviderType providerType = ExternalProviderType.MitId, string mockProviderCookieValue = "true", string? returnPath = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/account/authentication/{providerType}/verification/start")
        {
            Content = JsonContent.Create(new { ReturnPath = returnPath })
        };
        request.Headers.TryAddWithoutValidation("Cookie", $"{OAuthProviderFactory.UseMockProviderCookieName}={mockProviderCookieValue}");

        var response = await authenticatedHttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var startResponse = await response.Content.ReadFromJsonAsync<StartVerificationResponseBody>();
        return (startResponse!.AuthorizationUrl, ExtractSetCookieHeaders(response));
    }

    /// <summary>
    ///     Presents a verification callback as the given signed-in user, so a test can prove that a flow started by one
    ///     person cannot be completed by another.
    /// </summary>
    protected async Task<HttpResponseMessage> CallVerificationCallback(HttpClient authenticatedHttpClient, string callbackUrl, IEnumerable<string> cookies, ExternalProviderType providerType = ExternalProviderType.MitId, string mockProviderCookieValue = "true")
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"/api/account/authentication/{providerType}/verification/callback?code={Uri.EscapeDataString(queryParams["code"]!)}&state={Uri.EscapeDataString(queryParams["state"]!)}";
        var request = CreateRequestWithCookies(HttpMethod.Get, requestUrl, cookies, mockProviderCookieValue);

        return await authenticatedHttpClient.SendAsync(request);
    }

    protected ExternalIdentity? GetVerifiedIdentity(UserId userId)
    {
        using var scope = _webApplicationFactory.Services.CreateScope();
        var accountDbContext = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        return accountDbContext.Set<ExternalIdentity>()
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsEnumerable()
            .FirstOrDefault(ei => ei.UserId == userId && ei.Capabilities.HasFlag(ExternalIdentityCapabilities.Verification));
    }

    /// <summary>
    ///     Starts a flow without the mock provider, which the shared client sends on every request by default. Setting
    ///     any Cookie header on the request stops HttpClient from adding its own default Cookie header.
    /// </summary>
    protected async Task<HttpResponseMessage> StartFlowWithoutMockProvider(ExternalProviderType providerType, string flowType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/account/authentication/{providerType}/{flowType}/start");
        request.Headers.TryAddWithoutValidation("Cookie", "unrelated-cookie=value");
        return await NoRedirectHttpClient.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> CallCallback(string callbackUrl, IEnumerable<string> cookies, string flowType = "login", string mockProviderCookieValue = "true")
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"{uri.AbsolutePath}?code={Uri.EscapeDataString(queryParams["code"]!)}&state={Uri.EscapeDataString(queryParams["state"]!)}";
        var request = CreateRequestWithCookies(HttpMethod.Get, requestUrl, cookies, mockProviderCookieValue);

        return await NoRedirectHttpClient.SendAsync(request);
    }

    /// <summary>
    ///     Presents a flow's code and state to a callback route it was not started for, so the tests can prove that the
    ///     route only decides which handler runs and that every decision is made on the persisted flow.
    /// </summary>
    protected async Task<HttpResponseMessage> CallCallbackAtRoute(string callbackUrl, IEnumerable<string> cookies, ExternalProviderType providerType, string flowType, bool useMockProvider = true, string mockProviderCookieValue = "true")
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"/api/account/authentication/{providerType}/{flowType}/callback?code={Uri.EscapeDataString(queryParams["code"]!)}&state={Uri.EscapeDataString(queryParams["state"]!)}";

        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        foreach (var cookie in cookies)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie.Split(';')[0]);
        }

        // Setting any Cookie header stops HttpClient adding its own default, so the mock provider is opt in here
        request.Headers.TryAddWithoutValidation("Cookie", useMockProvider ? $"{OAuthProviderFactory.UseMockProviderCookieName}={mockProviderCookieValue}" : "unrelated-cookie=value");

        return await NoRedirectHttpClient.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> CallCallbackWithError(string callbackUrl, IEnumerable<string> cookies, string error, string? errorDescription = null)
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"{uri.AbsolutePath}?state={Uri.EscapeDataString(queryParams["state"]!)}&error={Uri.EscapeDataString(error)}";
        if (errorDescription is not null) requestUrl += $"&error_description={Uri.EscapeDataString(errorDescription)}";

        var request = CreateRequestWithCookies(HttpMethod.Get, requestUrl, cookies);
        return await NoRedirectHttpClient.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> CallCallbackWithoutCode(string callbackUrl, IEnumerable<string> cookies)
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"{uri.AbsolutePath}?state={Uri.EscapeDataString(queryParams["state"]!)}";
        var request = CreateRequestWithCookies(HttpMethod.Get, requestUrl, cookies);
        return await NoRedirectHttpClient.SendAsync(request);
    }

    protected string GetExternalLoginIdFromResponse(HttpResponseMessage startResponse)
    {
        var location = startResponse.Headers.Location!.ToString();
        return GetExternalLoginIdFromUrl(location);
    }

    protected string GetExternalLoginIdFromUrl(string url)
    {
        var uri = ToAbsoluteUri(url);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        var state = queryParams["state"];

        using var scope = _webApplicationFactory.Services.CreateScope();
        var externalAuthService = scope.ServiceProvider.GetRequiredService<ExternalAuthenticationService>();
        var externalLoginId = externalAuthService.GetExternalLoginIdFromState(state);
        return externalLoginId!.ToString();
    }

    protected void ExpireExternalLogin(string externalLoginId)
    {
        var expiredTime = TimeProvider.GetUtcNow().AddSeconds(-(ExternalLogin.ValidForSeconds + 1));
        Connection.Update("external_logins", "id", externalLoginId, [("created_at", expiredTime)]);
    }

    protected void TamperWithNonce(string externalLoginId)
    {
        Connection.Update("external_logins", "id", externalLoginId, [("nonce", "tampered-nonce-value")]);
    }

    protected TenantId InsertTenant()
    {
        var tenantId = TenantId.NewId();
        Connection.Insert("tenants", [
                ("id", tenantId.Value),
                ("created_at", TimeProvider.GetUtcNow()),
                ("modified_at", null),
                ("name", Faker.Company.CompanyName()),
                ("state", nameof(TenantState.Active)),
                ("logo", """{"Url":null,"Version":0}"""),
                ("plan", nameof(SubscriptionPlan.Basis)),
                ("rollout_bucket", 42)
            ]
        );

        Connection.Insert("subscriptions", [
                ("tenant_id", tenantId.Value),
                ("id", SubscriptionId.NewId().ToString()),
                ("created_at", TimeProvider.GetUtcNow()),
                ("modified_at", null),
                ("plan", nameof(SubscriptionPlan.Basis)),
                ("scheduled_plan", null),
                ("stripe_customer_id", null),
                ("stripe_subscription_id", null),
                ("current_price_amount", null),
                ("current_price_currency", null),
                ("current_period_end", null),
                ("cancel_at_period_end", false),
                ("first_payment_failed_at", null),
                ("cancellation_reason", null),
                ("cancellation_feedback", null),
                ("payment_transactions", "[]"),
                ("payment_method", null),
                ("billing_info", null),
                ("has_drift_detected", false),
                ("drift_checked_at", null),
                ("drift_discrepancies", "[]")
            ]
        );
        return tenantId;
    }

    protected UserId InsertUser(string email, TenantId? tenantId = null, bool emailConfirmed = true)
    {
        var userId = UserId.NewId();
        Connection.Insert("users", [
                ("tenant_id", (tenantId ?? DatabaseSeeder.Tenant1.Id).ToString()),
                ("id", userId.ToString()),
                ("created_at", TimeProvider.GetUtcNow()),
                ("modified_at", null),
                ("email", email.ToLower()),
                ("email_confirmed", emailConfirmed),
                ("first_name", Faker.Name.FirstName()),
                ("last_name", Faker.Name.LastName()),
                ("title", null),
                ("avatar", JsonSerializer.Serialize(new Avatar())),
                ("role", nameof(UserRole.Member)),
                ("locale", "en-US"),
                ("external_identities", "[]"),
                ("rollout_bucket", 42)
            ]
        );
        return userId;
    }

    protected UserId InsertDeletedUser(string email, TenantId? tenantId = null)
    {
        var userId = InsertUser(email, tenantId);
        Connection.Update("users", "id", userId.ToString(), [("deleted_at", TimeProvider.GetUtcNow())]);
        return userId;
    }

    protected UserId InsertUserWithExternalIdentity(string email, ExternalProviderType providerType, string providerUserId, TenantId? tenantId = null)
    {
        var userId = InsertUser(email, tenantId);
        var identities = JsonSerializer.Serialize(new[] { new { Provider = providerType.ToString(), ProviderUserId = providerUserId } });
        Connection.Update("users", "id", userId.ToString(), [("external_identities", identities)]);
        return userId;
    }

    protected ExternalIdentityId InsertExternalIdentity(UserId userId, ExternalProviderType providerType, string providerUserId, TenantId? tenantId = null)
    {
        var issuer = $"https://mock.localhost/{providerType.ToString().ToLowerInvariant()}";
        var externalIdentity = ExternalIdentity.Create(tenantId ?? DatabaseSeeder.Tenant1.Id, userId, providerType, providerUserId, issuer, providerUserId);
        Connection.Insert("external_identities", [
                ("tenant_id", externalIdentity.TenantId.ToString()),
                ("id", externalIdentity.Id.ToString()),
                ("user_id", externalIdentity.UserId.ToString()),
                ("created_at", TimeProvider.GetUtcNow()),
                ("modified_at", null),
                ("provider", externalIdentity.Provider.ToString()),
                ("provider_user_id", externalIdentity.ProviderUserId),
                ("capabilities", externalIdentity.Capabilities.ToString()),
                ("issuer", externalIdentity.Issuer),
                ("subject", externalIdentity.Subject)
            ]
        );
        return externalIdentity.Id;
    }

    /// <summary>
    ///     Seeds an identity that has been verified with MitID and may therefore log in, carrying the evidence a
    ///     verification callback would have written. Seeded directly rather than by driving a verification flow
    ///     first, so a login test fails for a reason in the login path.
    /// </summary>
    protected ExternalIdentityId InsertVerifiedMitIdIdentity(UserId userId, string providerUserId, TenantId? tenantId = null)
    {
        var externalIdentityId = InsertExternalIdentity(userId, ExternalProviderType.MitId, providerUserId, tenantId);
        Connection.Update("external_identities", "id", externalIdentityId.ToString(), [
                ("capabilities", $"{nameof(ExternalIdentityCapabilities.Login)}, {nameof(ExternalIdentityCapabilities.Verification)}"),
                ("assurance_level", nameof(IdentityAssuranceLevel.Substantial)),
                ("verified_at", VerifiedAt),
                ("authenticated_at", AuthenticatedAt),
                ("verified_by_external_login_id", VerifiedByExternalLoginId.ToString())
            ]
        );
        return externalIdentityId;
    }

    protected long CountExternalIdentities(ExternalProviderType providerType, string providerUserId, TenantId? tenantId = null)
    {
        return Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM external_identities WHERE provider = @provider AND provider_user_id = @providerUserId AND tenant_id = @tenantId",
            [new { provider = providerType.ToString(), providerUserId, tenantId = (tenantId ?? DatabaseSeeder.Tenant1.Id).Value }]
        );
    }

    protected string GetExternalIdentityUserId(ExternalProviderType providerType, string providerUserId, TenantId? tenantId = null)
    {
        return Connection.ExecuteScalar<string>(
            "SELECT user_id FROM external_identities WHERE provider = @provider AND provider_user_id = @providerUserId AND tenant_id = @tenantId",
            [new { provider = providerType.ToString(), providerUserId, tenantId = (tenantId ?? DatabaseSeeder.Tenant1.Id).Value }]
        );
    }

    protected long GetSessionTenantId(UserId userId)
    {
        return Connection.ExecuteScalar<long>(
            "SELECT tenant_id FROM sessions WHERE user_id = @userId ORDER BY created_at DESC LIMIT 1", [new { userId = userId.ToString() }]
        );
    }

    [UsedImplicitly]
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        Connection.Close();
        _webApplicationFactory.Dispose();
    }

    private static string BuildStartUrl(ExternalProviderType providerType, string flowType, string? returnPath, string? locale, TenantId? preferredTenantId = null)
    {
        var url = $"/api/account/authentication/{providerType}/{flowType}/start";
        var queryParams = new List<string>();
        if (returnPath is not null) queryParams.Add($"returnPath={Uri.EscapeDataString(returnPath)}");
        if (locale is not null) queryParams.Add($"locale={locale}");
        if (preferredTenantId is not null) queryParams.Add($"preferredTenantId={preferredTenantId}");
        if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);
        return url;
    }

    private static Uri ToAbsoluteUri(string url)
    {
        var uri = new Uri(url, UriKind.RelativeOrAbsolute);
        return uri.IsAbsoluteUri ? uri : new Uri(new Uri(PublicUrl), url);
    }

    private static string[] ExtractSetCookieHeaders(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToArray() : [];
    }

    protected async Task<HttpResponseMessage> CallCallbackWithTamperedState(string callbackUrl, IEnumerable<string> cookies)
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"{uri.AbsolutePath}?code={Uri.EscapeDataString(queryParams["code"]!)}&state=garbage-tampered-data";
        var request = CreateRequestWithCookies(HttpMethod.Get, requestUrl, cookies);
        return await NoRedirectHttpClient.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> CallCallbackWithCrossedFlows(string callbackUrl, IEnumerable<string> crossedCookies)
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"{uri.AbsolutePath}?code={Uri.EscapeDataString(queryParams["code"]!)}&state={Uri.EscapeDataString(queryParams["state"]!)}";
        var request = CreateRequestWithCookies(HttpMethod.Get, requestUrl, crossedCookies);
        return await NoRedirectHttpClient.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> CallCallbackWithTamperedCookie(string callbackUrl, string tamperedCookieValue)
    {
        var uri = ToAbsoluteUri(callbackUrl);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var requestUrl = $"{uri.AbsolutePath}?code={Uri.EscapeDataString(queryParams["code"]!)}&state={Uri.EscapeDataString(queryParams["state"]!)}";
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("Cookie", $"__Host-external-login={tamperedCookieValue}");
        request.Headers.TryAddWithoutValidation("Cookie", $"{OAuthProviderFactory.UseMockProviderCookieName}=true");
        return await NoRedirectHttpClient.SendAsync(request);
    }

    private HttpClient CreateBrowserLikeClient()
    {
        var httpClient = _webApplicationFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        httpClient.DefaultRequestHeaders.Add("User-Agent", "TestBrowser/1.0");
        httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US");
        httpClient.DefaultRequestHeaders.Add("Cookie", $"{OAuthProviderFactory.UseMockProviderCookieName}=true");
        return httpClient;
    }

    private static UserInfo CreateUserInfo(User user, SessionId sessionId)
    {
        return new UserInfo
        {
            IsAuthenticated = true,
            Id = user.Id,
            TenantId = user.TenantId,
            SessionId = sessionId,
            Role = user.Role.ToString(),
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Title = user.Title,
            AvatarUrl = user.Avatar.Url,
            Locale = user.Locale
        };
    }

    private static HttpRequestMessage CreateRequestWithCookies(HttpMethod method, string requestUrl, IEnumerable<string> cookies, string mockProviderCookieValue = "true")
    {
        var request = new HttpRequestMessage(method, requestUrl);
        foreach (var cookie in cookies)
        {
            var cookieParts = cookie.Split(';')[0];
            request.Headers.TryAddWithoutValidation("Cookie", cookieParts);
        }

        request.Headers.TryAddWithoutValidation("Cookie", $"{OAuthProviderFactory.UseMockProviderCookieName}={mockProviderCookieValue}");
        return request;
    }

    private sealed record StartVerificationResponseBody(string AuthorizationUrl);
}
