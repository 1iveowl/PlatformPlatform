using System.Net;
using System.Net.Http.Json;
using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class StartExternalVerificationTests : ExternalAuthenticationTestBase
{
    [Fact]
    public async Task StartExternalVerification_WhenAuthenticated_ShouldReturnAuthorizationUrlAndBindTheFlowToTheUser()
    {
        // Act
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient);

        // Assert
        callbackUrl.Should().Contain("/api/account/authentication/MitId/verification/callback");
        callbackUrl.Should().Contain("code=mock-authorization-code");
        cookies.Should().Contain(cookie => cookie.StartsWith("__Host-external-login=", StringComparison.Ordinal));

        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        var boundUserId = Connection.ExecuteScalar<string>(
            "SELECT user_id FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        boundUserId.Should().Be(DatabaseSeeder.Tenant1Owner.Id.ToString());

        var flowType = Connection.ExecuteScalar<string>(
            "SELECT type FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        flowType.Should().Be(nameof(ExternalLoginType.Verification));

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalVerificationStarted");
    }

    [Fact]
    public async Task StartExternalVerification_WhenAReturnPathIsGiven_ShouldSendTheUserBackThereAfterTheCallback()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, returnPath: "/user/profile");

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/user/profile");
    }

    [Fact]
    public async Task StartExternalVerification_WhenAnonymous_ShouldReturnUnauthorized()
    {
        // Act
        var response = await NoRedirectHttpClient.PostAsJsonAsync("/api/account/authentication/MitId/verification/start", new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_logins", []).Should().Be(0);
    }

    [Theory]
    [InlineData(ExternalProviderType.Google)]
    [InlineData(ExternalProviderType.Entra)]
    public async Task StartExternalVerification_WhenProviderDoesNotSupportVerification_ShouldReturnBadRequest(ExternalProviderType providerType)
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync($"/api/account/authentication/{providerType}/verification/start", new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_logins", []).Should().Be(0);
    }

    [Fact]
    public async Task StartExternalVerification_WhenCalledWithGet_ShouldNotStartAFlow()
    {
        // A cross-site navigation can only issue a GET, so the flow must be unreachable that way. Routing answers with
        // a not found rather than a method not allowed, because no GET is mapped on this path at all.

        // Act
        var response = await AuthenticatedOwnerHttpClient.GetAsync("/api/account/authentication/MitId/verification/start");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_logins", []).Should().Be(0);
    }
}
