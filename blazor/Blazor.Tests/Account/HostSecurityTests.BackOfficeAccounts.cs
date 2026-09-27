using System.Net;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The back office's accounts list on the back-office host. The page prerenders the shell, the toolbar and the list's frame for
// any forwarded back-office identity, admin or not, and loads the rows in the browser; without an identity it sends the
// browser to the platform login, and the app host does not serve it.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("User", false)]
    public async Task BackOfficeAccounts_WhenABackOfficeIdentityIsForwarded_ShouldPrerenderTheToolbarAndTheListFrame(string name, bool isAdmin)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/accounts?plans=%5B%22Premium%22%5D&driftDetected=true",
            fixture.ProtectBackOfficeIdentity(name, isAdmin)
        );

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain("data-testid=\"back-office-accounts\"");
        html.Should().Contain("aria-current=\"page\" data-testid=\"sidebar-nav-accounts\"");
        html.Should().Contain("data-testid=\"accounts-search\"").And.Contain("data-testid=\"accounts-plan-premium\"").And.Contain("data-testid=\"accounts-status-free\"");
        html.Should().Contain("data-testid=\"accounts-grid\"");
        html.Should().NotContain("data-testid=\"billing-banners\"");
    }

    [Fact]
    public async Task BackOfficeAccounts_WhenNoIdentityIsForwarded_ShouldRedirectToThePlatformLogin()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/accounts?driftDetected=true", null);

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/.auth/login/aad?post_login_redirect_uri=%2Fblazor%2Fback-office%2Faccounts%3FdriftDetected%3Dtrue");
    }

    [Fact]
    public async Task BackOfficeAccounts_WhenRequestedOnTheAppHost_ShouldNotBeServed()
    {
        // Arrange: the app host, with a back-office identity that only the back-office host would accept
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/back-office/accounts");
        request.Headers.Add("X-Back-Office-Identity", fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        (await response.Content.ReadAsStringAsync()).Should().NotContain("back-office-accounts");
    }
}
