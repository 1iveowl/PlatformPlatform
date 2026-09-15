using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace Blazor.Tests.Account;

// Welcome gating through the real host: the authenticated home sends a user without a first name to welcome with itself as
// the destination, welcome renders the profile step, and an anonymous visit to welcome goes to login. Part of
// HostSecurityTests so it shares the one host the fixture starts.
public sealed partial class HostSecurityTests
{
    [Fact]
    public async Task AuthenticatedHome_WhenUserHasNoFirstName_ShouldRedirectToWelcome()
    {
        // Act
        using var response = await GetAuthenticatedPageAsync(fixture.Client, fixture.CreateToken("new@example.com", firstName: null));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.PathAndQuery.Should().Be("/blazor/welcome?returnPath=%2Fblazor%2Fapp");
    }

    [Fact]
    public async Task Welcome_WhenUserHasNoFirstName_ShouldRenderProfileStep()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/welcome?returnPath=%2Fblazor%2Fapp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("new@example.com", firstName: null));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"first-name\"").And.NotContain("data-testid=\"account-name\"");
    }

    [Fact]
    public async Task Welcome_WhenSetupIsDone_ShouldRedirectToSanitizedDestination()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/welcome?returnPath=%2F%2Fevil.example.com");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("done@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.PathAndQuery.Should().Be("/blazor/app");
    }

    [Fact]
    public async Task Welcome_WhenAnonymous_ShouldRedirectToLogin()
    {
        // Act
        using var response = await fixture.Client.GetAsync("blazor/welcome");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Be("/blazor/login?returnPath=%2Fblazor%2Fwelcome");
    }
}
