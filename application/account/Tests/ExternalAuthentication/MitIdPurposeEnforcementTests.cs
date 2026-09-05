using System.Net;
using System.Net.Http.Json;
using Account.Features.ExternalAuthentication.Commands;
using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class MitIdPurposeEnforcementTests : ExternalAuthenticationTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartAndComplete_WhenPurposesAreConfiguredIndependently_ShouldEnforceEachPurpose(bool verificationEnabled, bool loginEnabled)
    {
        // Arrange
        var (initialUrl, initialCookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient);
        await CallVerificationCallback(AuthenticatedOwnerHttpClient, initialUrl, initialCookies);
        var sessionCount = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions", []);
        var identityCount = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []);
        SetPurposes(verificationEnabled, loginEnabled);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var verificationStart = await AuthenticatedOwnerHttpClient.PostAsJsonAsync("/api/account/authentication/MitId/verification/start", new { });
        var loginStart = await NoRedirectHttpClient.GetAsync("/api/account/authentication/MitId/login/start");

        // Assert
        verificationStart.StatusCode.Should().Be(verificationEnabled ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        loginStart.StatusCode.Should().Be(loginEnabled ? HttpStatusCode.Redirect : HttpStatusCode.BadRequest);
        if (verificationEnabled)
        {
            var result = await verificationStart.Content.ReadFromJsonAsync<StartExternalVerificationResponse>();
            var callback = await CallVerificationCallback(AuthenticatedOwnerHttpClient, result!.AuthorizationUrl, verificationStart.Headers.GetValues("Set-Cookie"));
            callback.Headers.Location!.ToString().Should().Be("/");
        }

        if (loginEnabled)
        {
            var callbackUrl = loginStart.Headers.Location!.ToString();
            var callback = await CallCallback(callbackUrl, loginStart.Headers.GetValues("Set-Cookie"));
            callback.Headers.Location!.ToString().Should().Be("/");
            var loginId = GetExternalLoginIdFromUrl(callbackUrl);
            Connection.ExecuteScalar<string>("SELECT user_id FROM external_logins WHERE id = @id", [new { id = loginId }]).Should().Be(DatabaseSeeder.Tenant1Owner.Id.ToString());
        }

        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions", []).Should().Be(sessionCount + (loginEnabled ? 1 : 0));
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []).Should().Be(identityCount);
        (await NoRedirectHttpClient.GetAsync("/api/account/authentication/Google/login/start")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await NoRedirectHttpClient.GetAsync("/api/account/authentication/Entra/login/start")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_WhenPurposeIsDisabledAfterStart_ShouldRejectWithoutChangingIdentityEvidenceOrSessions(bool verification)
    {
        // Arrange
        var (initialUrl, initialCookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient);
        await CallVerificationCallback(AuthenticatedOwnerHttpClient, initialUrl, initialCookies);
        var before = GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id)!;
        var (callbackUrl, cookies) = verification
            ? await StartVerificationFlow(AuthenticatedOwnerHttpClient)
            : await StartLoginFlow(providerType: ExternalProviderType.MitId);
        var loginId = GetExternalLoginIdFromUrl(callbackUrl);
        var sessionCount = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions", []);
        var identityCount = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []);
        SetPurposes(!verification, verification);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var callback = verification
            ? await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies)
            : await CallCallback(callbackUrl, cookies);

        // Assert
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().StartWith("/error?error=");
        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = loginId }]).Should().Be(nameof(ExternalLoginResult.FlowNotSupported));
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions", []).Should().Be(sessionCount);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []).Should().Be(identityCount);
        var after = GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id)!;
        after.Id.Should().Be(before.Id);
        after.Capabilities.Should().Be(before.Capabilities);
        after.AssuranceLevel.Should().Be(before.AssuranceLevel);
        after.VerifiedAt.Should().Be(before.VerifiedAt);
        after.AuthenticatedAt.Should().Be(before.AuthenticatedAt);
        after.VerifiedByExternalLoginId.Should().Be(before.VerifiedByExternalLoginId!);
    }

    private void SetPurposes(bool verificationEnabled, bool loginEnabled)
    {
        var configuration = WebApplicationServices.GetRequiredService<IConfiguration>();
        configuration["OAuth:MitId:VerificationEnabled"] = verificationEnabled ? "true" : "false";
        configuration["OAuth:MitId:LoginEnabled"] = loginEnabled ? "true" : "false";
    }
}
