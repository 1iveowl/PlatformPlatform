using System.Net;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The back office's account detail on the back-office host. The page prerenders the shell and the loading state for any
// forwarded back-office identity and reads the account in the browser. Its route takes a whole number only, so a malformed
// tenant id is answered by the back office's not-found page with status 404; without an identity it sends the browser to the
// platform login, and the app host does not serve it.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("Admin", true, "")]
    [InlineData("User", false, "?tab=feature-flags")]
    public async Task BackOfficeAccountDetail_WhenABackOfficeIdentityIsForwarded_ShouldPrerenderTheShellAndTheLoadingState(string name, bool isAdmin, string query)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/accounts/1553841786776989696{query}", fixture.ProtectBackOfficeIdentity(name, isAdmin));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain("data-testid=\"back-office-account-detail\"");
        html.Should().Contain("data-testid=\"account-detail-loading\"");
        html.Should().Contain("aria-current=\"page\" data-testid=\"sidebar-nav-accounts\"");
        html.Should().NotContain("data-testid=\"back-office-not-found\"");
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("12.5")]
    [InlineData("99999999999999999999")]
    public async Task BackOfficeAccountDetail_WhenTheTenantIdIsMalformed_ShouldAnswer404InsideTheBackOffice(string tenantId)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/accounts/{tenantId}?tab=users", fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-not-found\"").And.Contain("data-testid=\"not-found-home\"");
        html.Should().NotContain("data-testid=\"back-office-account-detail\"");
    }

    [Fact]
    public async Task BackOfficeAccountDetail_WhenNoIdentityIsForwarded_ShouldRedirectToThePlatformLogin()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/accounts/42?tab=users", null);

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/.auth/login/aad?post_login_redirect_uri=%2Fblazor%2Fback-office%2Faccounts%2F42%3Ftab%3Dusers");
    }

    [Fact]
    public async Task BackOfficeAccountDetail_WhenRequestedOnTheAppHost_ShouldNotBeServed()
    {
        // Arrange: the app host, with a back-office identity that only the back-office host would accept
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/back-office/accounts/42");
        request.Headers.Add("X-Back-Office-Identity", fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        (await response.Content.ReadAsStringAsync()).Should().NotContain("back-office-account-detail");
    }
}
