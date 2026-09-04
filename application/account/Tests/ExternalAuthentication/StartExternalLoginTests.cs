using System.Net;
using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using SharedKernel.Tests;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class StartExternalLoginTests : ExternalAuthenticationTestBase
{
    [Fact]
    public async Task StartExternalLogin_WhenValidProvider_ShouldRedirectToAuthorizationUrl()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync(
            "/api/account/authentication/Google/login/start?returnPath=%2Fdashboard&locale=en-US"
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().Contain("/api/account/authentication/Google/login/callback");
        location.Should().Contain("code=mock-authorization-code");
        location.Should().Contain("state=");

        var externalLoginId = GetExternalLoginIdFromResponse(response);
        Connection.RowExists("external_logins", externalLoginId).Should().BeTrue();

        var loginType = Connection.ExecuteScalar<string>(
            "SELECT type FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginType.Should().Be(nameof(ExternalLoginType.Login));

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginStarted");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task StartExternalLogin_WhenNullReturnPathAndLocale_ShouldRedirectToAuthorizationUrl()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync(
            "/api/account/authentication/Google/login/start"
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().Contain("code=mock-authorization-code");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginStarted");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task StartExternalLogin_WhenEntraProviderWithMockProvider_ShouldRedirectToAuthorizationUrl()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync(
            "/api/account/authentication/Entra/login/start"
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().Contain("/api/account/authentication/Entra/login/callback");
        location.Should().Contain("code=mock-authorization-code");
        location.Should().Contain("state=");

        var externalLoginId = GetExternalLoginIdFromResponse(response);
        Connection.RowExists("external_logins", externalLoginId).Should().BeTrue();

        var providerType = Connection.ExecuteScalar<string>(
            "SELECT provider_type FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        providerType.Should().Be(nameof(ExternalProviderType.Entra));

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginStarted");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task StartExternalLogin_WhenEntraProviderIsNotConfigured_ShouldReturnBadRequest()
    {
        // Act
        var response = await StartFlowWithoutMockProvider(ExternalProviderType.Entra, "login");

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, "Provider 'Entra' is not configured.");
        TelemetryEventsCollectorSpy.CollectedEvents.Should().BeEmpty();
    }
}
