using System.Net;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The two routes the container platform probes. They are reached on the container's own port, so the probe URL carries no
// path base and no credential, and the probe is not a person: the response is the platform's plain health text, without the
// document headers, the per-request nonce and the antiforgery cookie every rendered page carries.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("internal-api/live")]
    [InlineData("internal-api/ready")]
    public async Task HealthEndpoint_WhenProbedOutsideThePathBaseWithoutCredentials_ShouldReportHealthy(string path)
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Theory]
    [InlineData("internal-api/live")]
    [InlineData("internal-api/ready")]
    public async Task HealthEndpoint_WhenProbed_ShouldCarryNoDocumentHeaderAndSetNoCookie(string path)
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync(path);

        // Assert
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
        response.Headers.Contains("X-Frame-Options").Should().BeFalse();
        response.Headers.Contains("Permissions-Policy").Should().BeFalse();
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Theory]
    [InlineData("blazor/internal-api/live")]
    [InlineData("blazor/internal-api/ready")]
    public async Task HealthEndpoint_WhenReachedUnderThePathBase_ShouldAnswerTheSameWay(string path)
    {
        // Arrange -- the gateway forwards everything under the path base, so the probe routes answer there too; they say
        // only that this process is responsive, which is what the platform's other systems expose on the same paths
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
