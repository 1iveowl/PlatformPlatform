using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The session end pages and the profile page through the real host: the error page names why a session ended in the
// request's culture and links to login, and the profile page requires a session and prerenders no profile data. Part of
// HostSecurityTests so it shares the one host the fixture starts.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("session_revoked", "en-US", "Session ended", "Your session was ended from another device.")]
    [InlineData("session_revoked", "da-DK", "Session afsluttet", "Din session blev afsluttet fra en anden enhed.")]
    [InlineData("session_expired", "en-US", "Session expired", "Your session has expired.")]
    [InlineData("session_not_found", "da-DK", "Session udløbet", "Din session er udløbet.")]
    public async Task ErrorPage_WhenSessionEnded_ShouldRenderLocalizedReasonWithLoginLink(string errorCode, string culture, string title, string message)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, $"blazor/error?error={errorCode}");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain($">{title}</h1>").And.Contain(message).And.Contain("href=\"/blazor/login\"");
    }

    [Fact]
    public async Task ProfilePage_WhenAnonymous_ShouldRedirectToLogin()
    {
        // Act
        using var response = await fixture.Client.GetAsync("blazor/user/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Be("/blazor/login?returnPath=%2Fblazor%2Fuser%2Fprofile");
    }

    [Fact]
    public async Task ProfilePage_WhenAuthenticated_ShouldPrerenderTheFrameWithoutProfileFields()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/user/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("profile@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain(">Profile</h1>").And.Contain("data-testid=\"profile-loading\"").And.NotContain("data-testid=\"first-name\"");
    }
}
