using System.Net;
using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

/// <summary>
///     Proves that the provider and flow policy is enforced by the backend rather than by the user interface. The
///     generic provider factory and the generic routes make every provider and flow combination reachable as soon as a
///     provider is registered, so a hidden button is not an authorization boundary.
/// </summary>
public sealed class ExternalAuthenticationPolicyEnforcementTests : ExternalAuthenticationTestBase
{
    [Fact]
    public async Task StartExternalSignup_WhenProviderIsMitId_ShouldReturnBadRequest()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync("/api/account/authentication/MitId/signup/start");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_logins", []).Should().Be(0);
        TelemetryEventsCollectorSpy.CollectedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task StartExternalLogin_WhenProviderIsMitId_ShouldStartTheFlow()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync("/api/account/authentication/MitId/login/start");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_logins WHERE type = 'Login' AND provider_type = 'MitId'", []).Should().Be(1);
    }

    [Fact]
    public async Task CompleteExternalSignup_WhenProviderIsMitIdAndNoFlowExists_ShouldRedirectToError()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync(
            "/api/account/authentication/MitId/signup/callback?code=any-code&state=any-state"
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=invalid_request");
        response.Headers.Should().NotContain(header => header.Key.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenTheFlowWasStartedForAnotherProvider_ShouldRedirectToErrorWithoutExchangingTheCode()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackAtRoute(callbackUrl, cookies, ExternalProviderType.MitId, "login");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=invalid_request");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.FlowNotSupported));
    }

    [Fact]
    public async Task CompleteExternalSignup_WhenTheFlowWasStartedAsALogin_ShouldRedirectToError()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        var tenantCountBefore = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM tenants", []);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackAtRoute(callbackUrl, cookies, ExternalProviderType.Google, "signup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=invalid_request");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.FlowNotSupported));

        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM tenants", []).Should().Be(tenantCountBefore);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenTheProviderSelectionChangedBetweenStartAndCallback_ShouldRedirectToError()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackAtRoute(callbackUrl, cookies, ExternalProviderType.Google, "login", false);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=invalid_request");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.FlowNotSupported));
    }
}
