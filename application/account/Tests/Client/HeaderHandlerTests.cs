using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Account.Client;
using Account.Features.Authentication.Queries;
using FluentAssertions;
using SharedKernel.Authentication;
using SharedKernel.Domain;
using Xunit;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Account.Tests.Client;

public sealed class HeaderHandlerTests
{
    [Fact]
    public void AccountApiHeaders_ShouldMatchServerHeaderNames()
    {
        // Assert
        AccountApiHeaders.AntiforgeryToken.Should().Be(AuthenticationTokenHttpKeys.AntiforgeryTokenHttpHeaderKey);
        AccountApiHeaders.UserFeatureFlags.Should().Be(AuthenticationTokenHttpKeys.UserFeatureFlagsHeaderKey);
        AccountApiHeaders.UnauthorizedReason.Should().Be(AuthenticationTokenHttpKeys.UnauthorizedReasonHeaderKey);
        AccountApiHeaders.Locale.Should().Be("X-Locale");
    }

    [Fact]
    public async Task LocaleHeaderHandler_WhenLocaleIsAvailable_ShouldAddLocaleHeader()
    {
        // Arrange
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(new LocaleHeaderHandler(() => "da-DK"), stubHandler);

        // Act
        await httpClient.GetAsync("/api/account/users/me");

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers.Should().ContainKey(AccountApiHeaders.Locale).WhoseValue.Should().Equal("da-DK");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task LocaleHeaderHandler_WhenLocaleIsEmpty_ShouldNotAddLocaleHeader(string? locale)
    {
        // Arrange
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(new LocaleHeaderHandler(() => locale), stubHandler);

        // Act
        await httpClient.GetAsync("/api/account/users/me");

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers.Should().NotContainKey(AccountApiHeaders.Locale);
    }

    [Fact]
    public async Task LocaleHeaderHandler_WhenRequestAlreadyHasLocale_ShouldKeepRequestLocale()
    {
        // Arrange
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(new LocaleHeaderHandler(() => "da-DK"), stubHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/users/me");
        request.Headers.Add(AccountApiHeaders.Locale, "en-US");

        // Act
        await httpClient.SendAsync(request);

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers[AccountApiHeaders.Locale].Should().Equal("en-US");
    }

    [Fact]
    public async Task LocaleHeaderHandler_WhenCreatedFromCurrentUiCulture_ShouldSendCurrentUiCultureName()
    {
        // Arrange
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(LocaleHeaderHandler.FromCurrentUiCulture(), stubHandler);
        CultureInfo.CurrentUICulture = new CultureInfo("da-DK");

        // Act
        await httpClient.GetAsync("/api/account/users/me");

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers[AccountApiHeaders.Locale].Should().Equal("da-DK");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task AntiforgeryHeaderHandler_WhenMethodChangesState_ShouldAddAntiforgeryToken(string method)
    {
        // Arrange
        var tokenSource = new StubAntiforgeryTokenSource("antiforgery-token");
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(new AntiforgeryHeaderHandler(tokenSource), stubHandler);

        // Act
        await httpClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/account/users/bulk-delete"));

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers[AccountApiHeaders.AntiforgeryToken].Should().Equal("antiforgery-token");
        tokenSource.RequestCount.Should().Be(1);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task AntiforgeryHeaderHandler_WhenMethodIsSafe_ShouldNotAddTokenOrConsultSource(string method)
    {
        // Arrange
        var tokenSource = new StubAntiforgeryTokenSource("antiforgery-token");
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(new AntiforgeryHeaderHandler(tokenSource), stubHandler);

        // Act
        await httpClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/account/users/me"));

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers.Should().NotContainKey(AccountApiHeaders.AntiforgeryToken);
        tokenSource.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task AntiforgeryHeaderHandler_WhenBearerOnlyGet_ShouldSendNoAntiforgeryTokenAndNoCookie()
    {
        // Arrange
        var tokenSource = new StubAntiforgeryTokenSource("antiforgery-token");
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var httpClient = CreateHttpClient(new LocaleHeaderHandler(() => "en-US") { InnerHandler = new AntiforgeryHeaderHandler(tokenSource) { InnerHandler = stubHandler } });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/bootstrap");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "access-token");

        // Act
        await httpClient.SendAsync(request);

        // Assert
        var recordedRequest = stubHandler.Requests.Should().ContainSingle().Subject;
        recordedRequest.Headers["Authorization"].Should().Equal("Bearer access-token");
        recordedRequest.Headers.Should().NotContainKey(AccountApiHeaders.AntiforgeryToken);
        recordedRequest.Headers.Should().NotContainKey("Cookie");
        tokenSource.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task AntiforgeryHeaderHandler_WhenRequestAlreadyHasToken_ShouldKeepTokenAndNotConsultSource()
    {
        // Arrange
        var tokenSource = new StubAntiforgeryTokenSource("antiforgery-token");
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(new AntiforgeryHeaderHandler(tokenSource), stubHandler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/authentication/logout");
        request.Headers.Add(AccountApiHeaders.AntiforgeryToken, "request-token");

        // Act
        await httpClient.SendAsync(request);

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers[AccountApiHeaders.AntiforgeryToken].Should().Equal("request-token");
        tokenSource.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AntiforgeryHeaderHandler_WhenTokenIsEmpty_ShouldNotAddAntiforgeryToken(string? antiforgeryToken)
    {
        // Arrange
        var tokenSource = new StubAntiforgeryTokenSource(antiforgeryToken);
        var stubHandler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(new AntiforgeryHeaderHandler(tokenSource), stubHandler);

        // Act
        await httpClient.PostAsync("/api/account/authentication/logout", null);

        // Assert
        stubHandler.Requests.Should().ContainSingle().Which.Headers.Should().NotContainKey(AccountApiHeaders.AntiforgeryToken);
        tokenSource.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task FeatureFlagsHeaderHandler_WhenResponseHasHeader_ShouldApplyHeaderToState()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap([]));
        var stubHandler = new StubHttpMessageHandler((_, _) =>
            {
                var response = StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add(AccountApiHeaders.UserFeatureFlags, "compact-view");
                return Task.FromResult(response);
            }
        );
        var httpClient = CreateHttpClient(new FeatureFlagsHeaderHandler(featureFlagState), stubHandler);

        // Act
        await httpClient.GetAsync("/api/account/users/me");

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
    }

    [Fact]
    public async Task FeatureFlagsHeaderHandler_WhenIdentityChangesWhileRequestIsInFlight_ShouldIgnoreHeader()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(["compact-view"]));
        var stubHandler = new StubHttpMessageHandler((_, _) =>
            {
                // The state moves to another identity after the request was sent and before its response arrives
                featureFlagState.Initialize(CreateAuthenticatedBootstrap(["beta-features"]));
                var response = StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add(AccountApiHeaders.UserFeatureFlags, "compact-view");
                return Task.FromResult(response);
            }
        );
        var httpClient = CreateHttpClient(new FeatureFlagsHeaderHandler(featureFlagState), stubHandler);

        // Act
        await httpClient.GetAsync("/api/account/users/me");

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
    }

    [Theory]
    [InlineData("GET", AccountApiRoutes.Bootstrap)]
    [InlineData("POST", AccountApiRoutes.SwitchTenant)]
    public async Task FeatureFlagsHeaderHandler_WhenResponseIsTheBootstrapOrTheTenantSwitch_ShouldLeaveStateUnchanged(string method, string path)
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(["compact-view"]));
        var stubHandler = new StubHttpMessageHandler((_, _) =>
            {
                var response = StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add(AccountApiHeaders.UserFeatureFlags, "beta-features");
                return Task.FromResult(response);
            }
        );
        var httpClient = CreateHttpClient(new FeatureFlagsHeaderHandler(featureFlagState), stubHandler);

        // Act
        await httpClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FeatureFlagsHeaderHandler_WhenResponseHasNoHeader_ShouldLeaveStateUnchanged(HttpStatusCode statusCode)
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(["compact-view"]));
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;
        var httpClient = CreateHttpClient(new FeatureFlagsHeaderHandler(featureFlagState), StubHttpMessageHandler.Returning(statusCode));

        // Act
        await httpClient.GetAsync("/api/account/users/me");

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
        changedCount.Should().Be(0);
    }

    private static HttpClient CreateHttpClient(DelegatingHandler handler, HttpMessageHandler innerHandler)
    {
        handler.InnerHandler = innerHandler;
        return CreateHttpClient(handler);
    }

    private static HttpClient CreateHttpClient(DelegatingHandler handler)
    {
        return StubHttpMessageHandler.CreateHttpClient(handler);
    }

    private static BootstrapResponse CreateAuthenticatedBootstrap(string[] featureFlags)
    {
        var user = new BootstrapUser(UserId.NewId(), new TenantId(1), "Owner", "owner@example.com", null, null, null, null, null, null, null, false, featureFlags);
        return new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), "antiforgery-token");
    }

    private sealed class StubAntiforgeryTokenSource(string? antiforgeryToken) : IAntiforgeryTokenSource
    {
        public int RequestCount { get; private set; }

        public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
        {
            RequestCount++;
            return ValueTask.FromResult(antiforgeryToken);
        }
    }
}
