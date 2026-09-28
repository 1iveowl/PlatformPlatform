using System.Net;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The back office's feature flag list and flag detail on the back-office host. Both pages prerender the shell and their frame
// for any forwarded back-office identity, admin or not, and read the flags in the browser; without an identity they send the
// browser to the platform login, and the app host does not serve them. The detail takes any one path segment and settles in
// the browser whether it names a flag, so an unknown key is not the host's 404.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("User", false)]
    public async Task BackOfficeFeatureFlags_WhenABackOfficeIdentityIsForwarded_ShouldPrerenderTheHeadingAndTheLoadingState(string name, bool isAdmin)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/feature-flags", fixture.ProtectBackOfficeIdentity(name, isAdmin));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain("data-testid=\"back-office-feature-flags\"");
        html.Should().Contain("aria-current=\"page\" data-testid=\"sidebar-nav-feature-flags\"");
        html.Should().Contain("data-testid=\"feature-flags-show-deleted\"").And.Contain("data-testid=\"feature-flags-loading\"");
        html.Should().NotContain("data-testid=\"back-office-not-found\"");
    }

    [Theory]
    [InlineData("Admin", true, "experimental-ui")]
    [InlineData("User", false, "not-a-registry-key")]
    public async Task BackOfficeFeatureFlagDetail_WhenABackOfficeIdentityIsForwarded_ShouldPrerenderTheShellAndTheLoadingState(string name, bool isAdmin, string flagKey)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/feature-flags/{flagKey}", fixture.ProtectBackOfficeIdentity(name, isAdmin));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain("data-testid=\"back-office-feature-flag-detail\"");
        html.Should().Contain("data-testid=\"feature-flag-detail-loading\"");
        html.Should().Contain("aria-current=\"page\" data-testid=\"sidebar-nav-feature-flags\"");
        html.Should().NotContain("data-testid=\"back-office-not-found\"");
    }

    [Theory]
    [InlineData("feature-flags")]
    [InlineData("feature-flags/experimental-ui")]
    public async Task BackOfficeFeatureFlags_WhenNoIdentityIsForwarded_ShouldRedirectToThePlatformLogin(string page)
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
    [InlineData("blazor/back-office/feature-flags", "back-office-feature-flags")]
    [InlineData("blazor/back-office/feature-flags/experimental-ui", "back-office-feature-flag-detail")]
    public async Task BackOfficeFeatureFlags_WhenRequestedOnTheAppHost_ShouldNotBeServed(string path, string testId)
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
