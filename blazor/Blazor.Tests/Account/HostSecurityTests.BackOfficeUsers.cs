using System.Net;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The back office's users list and user detail on the back-office host. Both pages prerender the shell and their frame for any
// forwarded back-office identity, admin or not, and read the users in the browser; without an identity they send the browser
// to the platform login, and the app host does not serve them. The detail takes any one path segment and settles in the
// browser whether it names a user.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("User", false)]
    public async Task BackOfficeUsers_WhenABackOfficeIdentityIsForwarded_ShouldPrerenderTheToolbarAndTheListFrame(string name, bool isAdmin)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/users?roles=%5B%22Owner%22%5D&activity=ActiveLast7Days",
            fixture.ProtectBackOfficeIdentity(name, isAdmin)
        );

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain("data-testid=\"back-office-users\"");
        html.Should().Contain("aria-current=\"page\" data-testid=\"sidebar-nav-users\"");
        html.Should().Contain("data-testid=\"users-search\"").And.Contain("data-testid=\"users-role-owner\"").And.Contain("data-testid=\"users-activity-inactiveover30days\"");
        html.Should().Contain("data-testid=\"users-grid\"");
    }

    [Theory]
    [InlineData("Admin", true, "")]
    [InlineData("User", false, "?tab=sessions")]
    public async Task BackOfficeUserDetail_WhenABackOfficeIdentityIsForwarded_ShouldPrerenderTheShellAndTheLoadingState(string name, bool isAdmin, string query)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0{query}", fixture.ProtectBackOfficeIdentity(name, isAdmin));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain("data-testid=\"back-office-user-detail\"");
        html.Should().Contain("data-testid=\"user-detail-loading\"");
        html.Should().Contain("aria-current=\"page\" data-testid=\"sidebar-nav-users\"");
        html.Should().NotContain("data-testid=\"back-office-not-found\"");
    }

    [Theory]
    [InlineData("users")]
    [InlineData("users/usr_01JMVAW4T4320KJ3A7EJMCG8R0?tab=logins")]
    public async Task BackOfficeUsers_WhenNoIdentityIsForwarded_ShouldRedirectToThePlatformLogin(string page)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/{page}", null);

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be($"/.auth/login/aad?post_login_redirect_uri={Uri.EscapeDataString($"/blazor/back-office/{page}")}");
    }

    [Theory]
    [InlineData("blazor/back-office/users", "back-office-users")]
    [InlineData("blazor/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0", "back-office-user-detail")]
    public async Task BackOfficeUsers_WhenRequestedOnTheAppHost_ShouldNotBeServed(string path, string testId)
    {
        // Arrange: the app host, with a back-office identity that only the back-office host would accept
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Back-Office-Identity", fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        (await response.Content.ReadAsStringAsync()).Should().NotContain(testId);
    }
}
