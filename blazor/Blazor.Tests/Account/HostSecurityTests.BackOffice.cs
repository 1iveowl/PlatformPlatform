using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;

namespace Blazor.Tests.Account;

// The back-office surface. A back-office page is served only on the back-office host and only for the protected identity the
// account API's back-office listener forwards; the X-MS-CLIENT-PRINCIPAL headers are never an identity here, however they
// arrive, the app user's token never opens a back-office page, and a back-office identity never opens an app page.
public sealed partial class HostSecurityTests
{
    private const string BackOfficePage = "blazor/back-office";
    private const string BackOfficeIdentityPage = "blazor/back-office/identity";
    private const string BackOfficeLoginRedirect = "/.auth/login/aad?post_login_redirect_uri=%2Fblazor%2Fback-office";

    [Fact]
    public async Task BackOfficePage_WhenNoIdentityIsForwarded_ShouldRedirectToThePlatformLogin()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, null);

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(BackOfficeLoginRedirect);
    }

    [Fact]
    public async Task BackOfficePage_WhenAdminIdentityIsForwarded_ShouldShowTheNameAndTheAdminMarker()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficeIdentityPage, fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-name\">Admin<");
        html.Should().Contain("data-testid=\"back-office-admin-marker\">Admin<");
    }

    [Fact]
    public async Task BackOfficePage_WhenNonAdminIdentityIsForwarded_ShouldServeThePageWithTheAdminMarkerOff()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficeIdentityPage, fixture.ProtectBackOfficeIdentity("User", false));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-name\">User<");
        html.Should().Contain("data-testid=\"back-office-admin-marker\">Not admin<");
    }

    [Fact]
    public async Task BackOfficePage_WhenServed_ShouldCarryTheNoncePolicyOfTheBackOfficeOriginAndNoAppShell()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.Should().Contain($"connect-src https://{HostFixture.BackOfficeHost}");
        policy.Should().NotContain(HostFixture.PublicHost);
        policy.Should().MatchRegex("script-src-elem [^;]*'nonce-[^']+'");
        policy.Should().Contain("worker-src 'self'").And.NotContain("unsafe-inline");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.StartsWith("__Host-xsrf-token=", StringComparison.Ordinal));
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-back-office=\"\"");
        html.Should().NotContain("rel=\"manifest\"");
    }

    [Fact]
    public async Task BackOfficePage_WhenOnlyTheAppTokenIsSent_ShouldRedirectToThePlatformLogin()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("app-user@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(BackOfficeLoginRedirect);
    }

    [Fact]
    public async Task BackOfficePage_WhenTheIdentityWasProtectedWithAnotherKeyRing_ShouldRedirectToThePlatformLogin()
    {
        // Arrange
        var foreignIdentity = fixture.ProtectBackOfficeIdentity("Admin", true, new EphemeralDataProtectionProvider());
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, foreignIdentity);

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(BackOfficeLoginRedirect);
    }

    [Fact]
    public async Task BackOfficePage_WhenPrincipalHeadersArriveWithAForgedBackOfficeForwardedHost_ShouldRedirectToThePlatformLogin()
    {
        // Arrange: the gateway's /blazor/ route with a forwarded host naming the back-office host and forged principal headers
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, null);
        HostFixture.AddPrincipalHeaders(request, "Forged");

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(BackOfficeLoginRedirect);
    }

    [Fact]
    public async Task BackOfficePage_WhenPrincipalHeadersAreSentStraightToTheHostUnderTheBackOfficeName_ShouldRedirectToThePlatformLogin()
    {
        // Arrange: no proxy in front, the back-office name as the Host header
        using var request = new HttpRequestMessage(HttpMethod.Get, BackOfficePage);
        request.Headers.Host = HostFixture.BackOfficeHost;
        request.Headers.Accept.ParseAdd("text/html");
        HostFixture.AddPrincipalHeaders(request, "Forged");

        // Act
        using var response = await fixture.DirectClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(BackOfficeLoginRedirect);
    }

    [Fact]
    public async Task BackOfficePage_WhenRequestedOnTheAppHostWithAnIdentity_ShouldNotBeFound()
    {
        // Arrange: principal headers and even a valid protected identity through the app host
        using var request = new HttpRequestMessage(HttpMethod.Get, BackOfficePage);
        request.Headers.Add(SharedKernel.Authentication.BackOfficeIdentity.ForwardedBackOfficeIdentity.HeaderName, fixture.ProtectBackOfficeIdentity("Admin", true));
        HostFixture.AddPrincipalHeaders(request, "Forged");

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("back-office-name");
    }

    [Fact]
    public async Task AppPage_WhenOnlyABackOfficeIdentityIsSentOnTheAppHost_ShouldRedirectToTheAppLogin()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/app");
        request.Headers.Add(SharedKernel.Authentication.BackOfficeIdentity.ForwardedBackOfficeIdentity.HeaderName, fixture.ProtectBackOfficeIdentity("Admin", true));
        HostFixture.AddPrincipalHeaders(request, "Admin");

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/blazor/login?returnPath=");
    }

    [Theory]
    [InlineData("blazor/app")]
    [InlineData("blazor/login")]
    [InlineData("blazor/")]
    public async Task AppPage_WhenRequestedOnTheBackOfficeHost_ShouldNotBeFound(string path)
    {
        // Arrange: a signed-in back-office identity, and the app user's token as well
        using var request = HostFixture.CreateBackOfficeRequest(path, fixture.ProtectBackOfficeIdentity("Admin", true));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("app-user@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("blazor/service-worker.js")]
    [InlineData("blazor/manifest.webmanifest")]
    public async Task AppShellFile_WhenRequestedOnTheBackOfficeHost_ShouldNotBeFound(string path)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(path, fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
