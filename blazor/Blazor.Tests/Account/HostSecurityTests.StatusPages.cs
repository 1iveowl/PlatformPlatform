using System.Net;
using System.Net.Http.Headers;
using Blazor.Host;
using Blazor.Host.Components.Pages.Development;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The not-found and error pages through the real host: the public layout without a session, the authenticated shell with
// one, and the exception details the error page reveals in Development only. Part of HostSecurityTests so it shares the one
// host the fixture starts.
public sealed partial class HostSecurityTests
{
    private const string ShellMarker = "data-testid=\"app-shell\"";
    private const string PublicNavigationMarker = "data-testid=\"public-nav\"";

    [Theory]
    [InlineData("blazor/does-not-exist", "en-US", "Page not found", "Go to home")]
    [InlineData("blazor/app/deeper/route/that/does/not/exist", "da-DK", "Siden blev ikke fundet", "Gå til forsiden")]
    public async Task NotFoundPage_WhenAnonymous_ShouldRenderInThePublicLayoutWithTheThemeMenu(string path, string culture, string heading, string home)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.Should().Contain(PublicNavigationMarker).And.Contain($">{heading}</h1>").And.Contain(home).And.Contain("href=\"/blazor/\"");
        html.Should().Contain("data-theme-menu").And.NotContain(ShellMarker).And.NotContain("<!--Blazor:{");
    }

    [Theory]
    [InlineData("en-US", "Page not found")]
    [InlineData("da-DK", "Siden blev ikke fundet")]
    public async Task NotFoundPage_WhenAuthenticated_ShouldRenderInsideTheShellWithHomeLinkToTheApp(string locale, string heading)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/app/does/not/exist");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("not-found@example.com", locale: locale));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.Should().Contain(ShellMarker).And.Contain($">{heading}</h1>").And.Contain("href=\"/blazor/app\"").And.NotContain(PublicNavigationMarker);
    }

    [Fact]
    public async Task ErrorPage_WhenRenderingThrowsInDevelopment_ShouldShowTheExceptionDetailsAndRetryTheFailedPage()
    {
        // Act
        using var response = await fixture.Client.GetAsync("blazor/development/throw?attempt=1");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        html.Should().Contain(PublicNavigationMarker).And.Contain(">Something went wrong</h1>").And.Contain("Show details");
        html.Should().Contain(ThrowingPage.FailureMessage).And.Contain("data-testid=\"error-stack-trace\"").And.NotContain("data-testid=\"error-reference-id\"");
        html.Should().Contain("href=\"/blazor/development/throw?attempt=1\"");
    }

    [Fact]
    public async Task ErrorPage_WhenRenderingThrowsForASignedInUser_ShouldRenderInsideTheShell()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/development/throw");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("error@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        html.Should().Contain(ShellMarker).And.Contain(">Something went wrong</h1>").And.NotContain(PublicNavigationMarker);
    }

    [Fact]
    public async Task ErrorPage_WhenHostIsNotDevelopment_ShouldShowOnlyTheReferenceId()
    {
        // Arrange
        await using var productionHost = HostApplication.Build(["--environment", "Production", "--urls", "http://127.0.0.1:0"], fixture.TokenSigningClient);
        await productionHost.StartAsync();
        using var client = CreateHostClient(productionHost);

        // Act
        using var response = await client.GetAsync("blazor/Error");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain(PublicNavigationMarker).And.Contain(">Something went wrong</h1>").And.Contain("data-testid=\"error-reference-id\"");
        html.Should().NotContain("data-testid=\"error-stack-trace\"").And.NotContain("data-testid=\"error-exception-message\"");
    }
}
