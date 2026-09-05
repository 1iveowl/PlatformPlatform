using System.Net;
using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class RejectedVerificationProfileTests() : ExternalAuthenticationTestBase(services => services.AddKeyedSingleton("mock-mitid", CreateRejectedProvider()))
{
    [Fact]
    public async Task CompleteExternalVerification_WhenProviderRejectsProfile_ShouldCreateNoIdentityOrSession()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient);
        var loginId = GetExternalLoginIdFromUrl(callbackUrl);
        var sessionCount = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions", []);
        var identityCount = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=authentication_failed");
        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = loginId }]).Should().Be(nameof(ExternalLoginResult.CodeExchangeFailed));
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions", []).Should().Be(sessionCount);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []).Should().Be(identityCount);
        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
    }

    private static IOAuthProvider CreateRejectedProvider()
    {
        var provider = Substitute.For<IOAuthProvider>();
        provider.BuildAuthorizationUrl(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => $"{call.ArgAt<string>(3)}?code=synthetic-code&state={Uri.EscapeDataString(call.ArgAt<string>(0))}");
        provider.ExchangeCodeForTokensAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResponse("synthetic-access-token", "rejected-id-token", 3600));
        provider.GetUserProfileAsync(Arg.Any<OAuthTokenResponse>(), Arg.Any<CancellationToken>()).Returns((OAuthUserProfile?)null);
        return provider;
    }
}
