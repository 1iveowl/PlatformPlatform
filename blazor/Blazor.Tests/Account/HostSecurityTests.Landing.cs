using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The landing page and the authenticated home page through the real host: the marketing content and the calls to action for
// an anonymous visitor in both cultures, the server-side redirect for a signed-in one, and the greeting and empty state
// prerendered inside the shell. Part of HostSecurityTests so it shares the one host the fixture starts.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("en-US", "Welcome to PlatformPlatform", "Get started", "Log in")]
    [InlineData("da-DK", "Velkommen til PlatformPlatform", "Kom i gang", "Log ind")]
    public async Task LandingPage_WhenAnonymous_ShouldRenderTheReactContentAndCallsToAction(string culture, string heading, string signUp, string logIn)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain($"<h1>{heading}</h1>").And.Contain($">{signUp}</a>").And.Contain($">{logIn}</a>");
        html.Should().Contain("PlatformPlatform").And.Contain("href=\"/blazor/signup\"").And.Contain("href=\"/blazor/login\"");
        // The footer's legal links, and no runtime preload link on a static public page (the import map is on every page)
        html.Should().Contain("href=\"/blazor/legal\"").And.Contain("href=\"/blazor/legal/dpa\"").And.NotContain("<link href=\"/blazor/_framework/");
    }

    [Fact]
    public async Task LandingPage_WhenAuthenticated_ShouldRedirectToTheAuthenticatedHomeWithoutRenderingTheContent()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("landing@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().EndWith("/blazor/app");
        html.Should().NotContain("Welcome to PlatformPlatform");
    }

    [Theory]
    [InlineData("en-US", "Your workspace", "Your dashboard is empty")]
    [InlineData("da-DK", "Dit arbejdsområde", "Dit dashboard er tomt")]
    public async Task HomePage_WhenAuthenticated_ShouldPrerenderTheGreetingAndTheEmptyStateInsideTheShell(string locale, string heading, string emptyState)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/app");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("home@example.com", locale: locale));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain(ShellMarker).And.Contain($"<h1>{heading}</h1>").And.Contain($">{emptyState}</h2>");
        html.Should().Contain("class=\"home-greeting\"").And.Contain("class=\"home-overview\"");
    }
}
