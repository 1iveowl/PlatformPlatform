using System.Net;
using System.Net.Http.Json;
using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth.Mock;
using FluentAssertions;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class ExternalLoginDestinationTests : ExternalAuthenticationTestBase
{
    private const string Blazor = "Blazor";
    private const string MockIdentityCookie = "identity:person-identifier";

    [Fact]
    public async Task CompleteExternalLogin_WhenBlazorFlowSucceeds_ShouldRedirectToTheBlazorHome()
    {
        // Arrange
        InsertLoginUser();
        var (callbackUrl, cookies) = await StartLoginFlow(edition: Blazor);

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/blazor/app");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenBlazorFlowHasALocalReturnPath_ShouldKeepItsQueryAndFragment()
    {
        // Arrange
        InsertLoginUser();
        var (callbackUrl, cookies) = await StartLoginFlow("/blazor/users?search=a%20b&page=2#top", edition: Blazor);

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/blazor/users?search=a%20b&page=2#top");
    }

    [Theory]
    [InlineData("/blazor/../dashboard")]
    [InlineData("/blazor/%2e%2e/dashboard")]
    [InlineData("/blazor/..%2fdashboard")]
    [InlineData("/blazor//evil.example.com")]
    [InlineData("//evil.example.com/blazor/app")]
    [InlineData("/blazor/\\evil.example.com")]
    [InlineData("/blazor/%5cevil.example.com")]
    [InlineData("https://evil.example.com/blazor/app")]
    [InlineData("/blazor/app\t")]
    [InlineData("/blazor/app%zz")]
    [InlineData("/blazorx/app")]
    [InlineData("/dashboard")]
    [InlineData("/dashboard?returnPath=/blazor/app")]
    public async Task CompleteExternalLogin_WhenBlazorReturnPathIsHostile_ShouldRedirectToTheBlazorHome(string returnPath)
    {
        // Arrange
        InsertLoginUser();
        var (callbackUrl, cookies) = await StartLoginFlow(returnPath, edition: Blazor);

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/blazor/app");
    }

    [Theory]
    [InlineData("/dashboard?tab=1", "/dashboard?tab=1")]
    [InlineData("/users/../admin", "/")]
    [InlineData("/%2e%2e/admin", "/")]
    [InlineData("//evil.example.com", "/")]
    [InlineData("/\t/evil.example.com", "/")]
    [InlineData("https://evil.example.com", "/")]
    public async Task CompleteExternalLogin_WhenReactFlow_ShouldKeepTheReactDefaultsAndRejectHostileReturnPaths(string returnPath, string expectedLocation)
    {
        // Arrange
        InsertLoginUser();
        var (callbackUrl, cookies) = await StartLoginFlow(returnPath);

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(expectedLocation);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenProviderDeniesABlazorFlow_ShouldRedirectToTheBlazorErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow("/blazor/users", edition: Blazor);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallCallbackWithError(callbackUrl, cookies, "access_denied", "The secret description of the refusal");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().Be($"/blazor/error?error=access_denied&id={externalLoginId}");
        location.Should().NotContain("secret");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenBlazorCallbackIsRefusedInternally_ShouldRedirectToTheBlazorErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow(edition: Blazor);

        // Act
        var response = await CallCallbackWithoutCode(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/blazor/error?error=authentication_failed&id=");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenBlazorFlowIsReplayed_ShouldRedirectToTheBlazorErrorPage()
    {
        // Arrange
        InsertLoginUser();
        var (callbackUrl, cookies) = await StartLoginFlow(edition: Blazor);
        await CallCallback(callbackUrl, cookies);

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/blazor/error?error=authentication_failed&id=");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenBlazorFlowHasExpired_ShouldRedirectToTheBlazorErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow(edition: Blazor);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        ExpireExternalLogin(externalLoginId);

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"/blazor/error?error=session_expired&id={externalLoginId}");
        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.LoginExpired));
    }

    [Theory]
    [InlineData(ExternalProviderType.Entra, "login")]
    [InlineData(ExternalProviderType.Google, "signup")]
    public async Task CompleteExternalLogin_WhenBlazorFlowReachesAnotherProviderOrPurpose_ShouldRedirectToTheBlazorErrorPage(ExternalProviderType providerType, string flowType)
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow(edition: Blazor);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallCallbackAtRoute(callbackUrl, cookies, providerType, flowType);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"/blazor/error?error=invalid_request&id={externalLoginId}");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenStateIsInvalid_ShouldUseTheSameOriginFallback()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow(edition: Blazor);

        // Act
        var response = await CallCallbackWithTamperedState(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=invalid_request&id=");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenFlowCookieIsMissing_ShouldUseTheSameOriginFallback()
    {
        // Arrange
        var (callbackUrl, _) = await StartLoginFlow("/blazor/users", edition: Blazor);

        // Act
        var response = await CallCallbackWithCrossedFlows(callbackUrl, []);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=authentication_failed&id=");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenFlowCookieIsCorrupt_ShouldUseTheSameOriginFallback()
    {
        // Arrange
        var (callbackUrl, _) = await StartLoginFlow("/blazor/users", edition: Blazor);

        // Act
        var response = await CallCallbackWithTamperedCookie(callbackUrl, "corrupt-cookie-value");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=authentication_failed&id=");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenAnotherFlowOverlaps_ShouldNotBorrowItsDestination()
    {
        // Arrange
        var (reactCallbackUrl, _) = await StartLoginFlow("/dashboard");
        var (_, blazorCookies) = await StartLoginFlow("/blazor/users", edition: Blazor);

        // Act
        var response = await CallCallbackWithCrossedFlows(reactCallbackUrl, blazorCookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/error?error=authentication_failed&id=");
        location.Should().NotContain("/blazor");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenRestartedFlowHasNoReturnPath_ShouldNotInheritAStaleOne()
    {
        // Arrange
        InsertLoginUser();
        await StartLoginFlow("/blazor/users", edition: Blazor);
        var (callbackUrl, cookies) = await StartLoginFlow(edition: Blazor);
        string[] cookiesWithStaleReturnPath = [..cookies, "__Host-return-path=%2Fblazor%2Fusers"];

        // Act
        var response = await CallCallback(callbackUrl, cookiesWithStaleReturnPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/blazor/app");
    }

    [Theory]
    [InlineData("Angular")]
    [InlineData("1")]
    [InlineData("React,Blazor")]
    [InlineData("/blazor/error")]
    public async Task StartExternalLogin_WhenEditionIsNotSupported_ShouldReturnBadRequestWithoutStartingAFlow(string edition)
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync($"/api/account/authentication/Google/login/start?edition={Uri.EscapeDataString(edition)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_logins", []).Should().Be(0);
    }

    [Theory]
    [InlineData("da-dk", "da-DK")]
    [InlineData("en-US", "en-US")]
    public async Task CompleteExternalSignup_WhenBlazorFlowCarriesASupportedLocale_ShouldCreateTheUserInItAndRedirectToTheBlazorHome(string locale, string expectedLocale)
    {
        // Arrange
        var (callbackUrl, cookies) = await StartSignupFlow(locale: locale, edition: Blazor);

        // Act
        var response = await CallCallback(callbackUrl, cookies, "signup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/blazor/app");
        Connection.ExecuteScalar<string>("SELECT locale FROM users WHERE email = @email", [new { email = MockOAuthProvider.MockEmail }])
            .Should().Be(expectedLocale);
    }

    [Fact]
    public async Task CompleteExternalSignup_WhenLocaleIsNotSupported_ShouldNotStoreIt()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartSignupFlow(locale: "fr-FR", edition: Blazor);

        // Act
        await CallCallback(callbackUrl, cookies, "signup");

        // Assert
        Connection.ExecuteScalar<string>("SELECT locale FROM users WHERE email = @email", [new { email = MockOAuthProvider.MockEmail }])
            .Should().NotBe("fr-FR");
    }

    [Fact]
    public async Task CompleteExternalSignup_WhenBlazorSignupFindsAnExistingAccount_ShouldRedirectToTheBlazorErrorPage()
    {
        // Arrange
        InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartSignupFlow(edition: Blazor);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallCallback(callbackUrl, cookies, "signup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"/blazor/error?error=account_already_exists&id={externalLoginId}");
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenBlazorFlowSucceeds_ShouldReturnToTheBlazorReturnPath()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie, returnPath: "/blazor/user/profile", edition: Blazor);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/blazor/user/profile");
        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenBlazorReturnPathIsHostile_ShouldReturnToTheBlazorHome()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie, returnPath: "/blazor/%2e%2e/user/profile", edition: Blazor);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/blazor/app");
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenBlazorFlowIsCompletedByAnotherUser_ShouldRedirectToTheBlazorErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie, returnPath: "/blazor/user/profile", edition: Blazor);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallVerificationCallback(AuthenticatedMemberHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"/blazor/error?error=authentication_failed&id={externalLoginId}");
        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
        GetVerifiedIdentity(DatabaseSeeder.Tenant1Member.Id).Should().BeNull();
    }

    [Fact]
    public async Task StartExternalVerification_WhenEditionIsNotSupported_ShouldReturnBadRequestWithoutStartingAFlow()
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync("/api/account/authentication/MitId/verification/start?Edition=Angular", new { ReturnPath = "/blazor/user/profile" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_logins", []).Should().Be(0);
    }

    private void InsertLoginUser()
    {
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
    }
}
