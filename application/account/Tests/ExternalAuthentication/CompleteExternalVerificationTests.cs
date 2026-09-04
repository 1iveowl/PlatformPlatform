using System.Net;
using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth.Mock;
using FluentAssertions;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class CompleteExternalVerificationTests : ExternalAuthenticationTestBase
{
    private const string MitIdProviderUserId = "mock-mitid-person-identifier";
    private const string MockIdentityCookie = "identity:person-identifier";

    [Fact]
    public async Task CompleteExternalVerification_WhenValid_ShouldCreateAVerificationOnlyIdentity()
    {
        // Arrange
        var userId = DatabaseSeeder.Tenant1Owner.Id;
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        var verifiedIdentity = GetVerifiedIdentity(userId);
        verifiedIdentity.Should().NotBeNull();
        verifiedIdentity.Provider.Should().Be(ExternalProviderType.MitId);
        verifiedIdentity.ProviderUserId.Should().Be(MitIdProviderUserId);
        verifiedIdentity.AssuranceLevel.Should().Be(IdentityAssuranceLevel.Substantial);
        verifiedIdentity.VerifiedAt.Should().NotBeNull();
        verifiedIdentity.AuthenticatedAt.Should().NotBeNull();
        verifiedIdentity.VerifiedByExternalLoginId.Should().NotBeNull();

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalVerificationCompleted");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenValid_ShouldNotGrantLoginOrCreateASession()
    {
        // Arrange
        var userId = DatabaseSeeder.Tenant1Owner.Id;
        var sessionCountBefore = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE user_id = @userId", [new { userId = userId.ToString() }]);
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);

        // Act
        await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        var verifiedIdentity = GetVerifiedIdentity(userId);
        verifiedIdentity!.Capabilities.Should().Be(ExternalIdentityCapabilities.Verification);
        verifiedIdentity.Capabilities.HasFlag(ExternalIdentityCapabilities.Login).Should().BeFalse();

        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE user_id = @userId", [new { userId = userId.ToString() }]).Should().Be(sessionCountBefore);
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenCompletedByAnotherUser_ShouldRedirectToErrorWithoutVerifyingEither()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallVerificationCallback(AuthenticatedMemberHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=authentication_failed");

        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
        GetVerifiedIdentity(DatabaseSeeder.Tenant1Member.Id).Should().BeNull();

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.VerificationUserMismatch));
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheSessionIsGone_ShouldReportItAsRetryableRatherThanAMismatch()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallVerificationCallback(NoRedirectHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=session_expired");

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.VerificationSessionLost));
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheSameIdentityIsPresentedAgain_ShouldRefreshTheEvidence()
    {
        // Arrange
        var userId = DatabaseSeeder.Tenant1Owner.Id;
        var (firstCallbackUrl, firstCookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);
        await CallVerificationCallback(AuthenticatedOwnerHttpClient, firstCallbackUrl, firstCookies, mockProviderCookieValue: MockIdentityCookie);
        var firstExternalLoginId = GetVerifiedIdentity(userId)!.VerifiedByExternalLoginId!.ToString();

        var (secondCallbackUrl, secondCookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, secondCallbackUrl, secondCookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        var verifiedIdentity = GetVerifiedIdentity(userId);
        verifiedIdentity!.VerifiedByExternalLoginId!.ToString().Should().NotBe(firstExternalLoginId);

        Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM external_identities WHERE user_id = @userId", [new { userId = userId.ToString() }]
        ).Should().Be(1);
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheUserIsAlreadyVerifiedWithAnotherIdentity_ShouldRedirectToError()
    {
        // Arrange
        var userId = DatabaseSeeder.Tenant1Owner.Id;
        var (firstCallbackUrl, firstCookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);
        await CallVerificationCallback(AuthenticatedOwnerHttpClient, firstCallbackUrl, firstCookies, mockProviderCookieValue: MockIdentityCookie);

        const string anotherIdentityCookie = "identity:another-person";
        var (secondCallbackUrl, secondCookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: anotherIdentityCookie);
        var secondExternalLoginId = GetExternalLoginIdFromUrl(secondCallbackUrl);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, secondCallbackUrl, secondCookies, mockProviderCookieValue: anotherIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=authentication_failed");

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = secondExternalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.IdentityMismatch));

        GetVerifiedIdentity(userId)!.ProviderUserId.Should().Be(MitIdProviderUserId);
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheIdentityIsHeldByAnotherLiveUserInTheTenant_ShouldRedirectToError()
    {
        // Arrange
        var memberId = DatabaseSeeder.Tenant1Member.Id;
        var tenantId = DatabaseSeeder.Tenant1Member.TenantId;
        InsertExternalIdentity(memberId, ExternalProviderType.MitId, MitIdProviderUserId, tenantId);

        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=identity_already_linked");

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.IdentityHeldByAnotherUser));

        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
        CountExternalIdentities(ExternalProviderType.MitId, MitIdProviderUserId, tenantId).Should().Be(1);
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheIdentityIsHeldByASoftDeletedUserInTheTenant_ShouldTakeTheIdentityOver()
    {
        // Arrange
        var tenantId = DatabaseSeeder.Tenant1Owner.TenantId;
        var deletedUserId = InsertDeletedUser(Faker.Internet.Email(), tenantId);
        InsertExternalIdentity(deletedUserId, ExternalProviderType.MitId, MitIdProviderUserId, tenantId);

        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockIdentityCookie);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().NotBeNull();
        CountExternalIdentities(ExternalProviderType.MitId, MitIdProviderUserId, tenantId).Should().Be(1);
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheProviderReplaysAnOldAuthentication_ShouldRedirectToError()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockOAuthProvider.StaleAuthenticationValue);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.StaleAuthenticationValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=authentication_failed");

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.StaleAuthentication));

        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheAssuranceLevelIsBelowTheRequirement_ShouldRedirectToErrorWithoutVerifying()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockOAuthProvider.LowAssuranceValue);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.LowAssuranceValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=assurance_level_insufficient");

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.AssuranceLevelInsufficient));

        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenTheAuthenticationIsInTheFuture_ShouldRedirectToErrorWithoutVerifying()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockOAuthProvider.FutureAuthenticationValue);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallVerificationCallback(AuthenticatedOwnerHttpClient, callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.FutureAuthenticationValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=authentication_failed");

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.StaleAuthentication));

        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
    }

    [Fact]
    public async Task CompleteExternalVerification_WhenPresentedToTheLoginCallback_ShouldRedirectToErrorWithoutVerifying()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartVerificationFlow(AuthenticatedOwnerHttpClient, mockProviderCookieValue: MockIdentityCookie);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);

        // Act
        var response = await CallCallbackAtRoute(callbackUrl, cookies, ExternalProviderType.MitId, "login");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=invalid_request");

        Connection.ExecuteScalar<string>("SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }])
            .Should().Be(nameof(ExternalLoginResult.FlowNotSupported));

        GetVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id).Should().BeNull();
    }
}
