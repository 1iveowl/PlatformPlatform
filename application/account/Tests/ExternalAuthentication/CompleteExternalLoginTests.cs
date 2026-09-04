using System.Net;
using Account.Features.Authentication.Domain;
using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth;
using Account.Integrations.OAuth.Mock;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class CompleteExternalLoginTests : ExternalAuthenticationTestBase
{
    [Fact]
    public async Task CompleteExternalLogin_WhenValid_ShouldCreateSessionAndRedirect()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow("/dashboard");
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/dashboard");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Identity));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenEntraIdentityExists_ShouldCreateSessionWithEntraLoginMethod()
    {
        // Arrange
        const string entraProviderUserId = "mock-entra-entrauser";
        var userId = InsertUser($"entrauser{OAuthProviderFactory.MockEmailDomain}");
        InsertExternalIdentity(userId, ExternalProviderType.Entra, entraProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow("/dashboard", providerType: ExternalProviderType.Entra);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: $"{MockOAuthProvider.IdentityPrefix}entrauser:entrauser");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/dashboard");

        var loginMethod = Connection.ExecuteScalar<string>(
            "SELECT login_method FROM sessions WHERE user_id = @userId", [new { userId = userId.ToString() }]
        );
        loginMethod.Should().Be(nameof(LoginMethod.Entra));
        CountExternalIdentities(ExternalProviderType.Entra, entraProviderUserId).Should().Be(1);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Identity));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenEmailChangedAtProvider_ShouldLoginByIdentity()
    {
        // Arrange
        var previousEmail = Faker.Internet.Email();
        var userId = InsertUser(previousEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        Connection.ExecuteScalar<string>("SELECT email FROM users WHERE id = @id", [new { id = userId.ToString() }]).Should().Be(previousEmail.ToLower());
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(1);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Identity));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenProfileHasNoEmailAndIdentityMatches_ShouldLoginByIdentity()
    {
        // Arrange
        var userId = InsertUser(Faker.Internet.Email());
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.NoEmailValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        Connection.ExecuteScalar<string>("SELECT email FROM external_logins WHERE id = @id", [new { id = externalLoginId }]).Should().BeNull();

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Identity));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenProfileHasNoEmailAndUserEmailUnconfirmed_ShouldNotConfirmEmail()
    {
        // Arrange
        var userId = InsertUser(Faker.Internet.Email(), emailConfirmed: false);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.NoEmailValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        Connection.ExecuteScalar<long>("SELECT email_confirmed FROM users WHERE id = @id", [new { id = userId.ToString() }]).Should().Be(0);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenEmailChangedAtProviderAndUserEmailUnconfirmed_ShouldNotConfirmEmail()
    {
        // Arrange
        var previousEmail = Faker.Internet.Email();
        var userId = InsertUser(previousEmail, emailConfirmed: false);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        Connection.ExecuteScalar<long>("SELECT email_confirmed FROM users WHERE id = @id", [new { id = userId.ToString() }]).Should().Be(0);
        Connection.ExecuteScalar<string>("SELECT email FROM users WHERE id = @id", [new { id = userId.ToString() }]).Should().Be(previousEmail.ToLower());
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenIdentityHasNoLoginCapability_ShouldRedirectToErrorPage()
    {
        // Arrange
        var userId = InsertUser(Faker.Internet.Email());
        var externalIdentityId = InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        Connection.Update("external_identities", "id", externalIdentityId.ToString(), [("capabilities", nameof(ExternalIdentityCapabilities.Verification))]);
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.NoEmailValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=user_not_found");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.UserNotFound));
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE user_id = @userId", [new { userId = userId.ToString() }]).Should().Be(0);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenProfileHasNoEmailAndNoIdentity_ShouldRedirectToErrorPage()
    {
        // Arrange
        InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.NoEmailValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=user_not_found");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.UserNotFound));
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(0);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenEmailNotVerified_ShouldRedirectToErrorPage()
    {
        // Arrange
        InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: $"{MockOAuthProvider.FailurePrefix}email_not_verified");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=authentication_failed");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.CodeExchangeFailed));

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenUserNotFound_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=user_not_found");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenIdentityMismatch_ShouldRedirectToErrorPage()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, "different-provider-user-id");
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=authentication_failed");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.IdentityMismatch));
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(0);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE user_id = @userId", [new { userId = userId.ToString() }]).Should().Be(0);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenOAuthError_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackWithError(callbackUrl, cookies, "access_denied", "User denied access");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=access_denied");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenMissingCode_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackWithoutCode(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=authentication_failed");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenMissingState_ShouldRedirectToErrorPage()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync("/api/account/authentication/Google/login/callback");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=invalid_request");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenFlowAlreadyCompleted_ShouldRedirectToErrorPage()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        await CallCallback(callbackUrl, cookies);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=authentication_failed");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenExpired_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        ExpireExternalLogin(externalLoginId);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=session_expired");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.LoginExpired));

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenNonceMismatch_ShouldRedirectToErrorPage()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TamperWithNonce(externalLoginId);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=authentication_failed");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.NonceMismatch));

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenUserHasNoExternalIdentity_ShouldLinkIdentityAndCreateSession()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(userId.ToString());
        Connection.ExecuteScalar<string>("SELECT capabilities FROM external_identities WHERE user_id = @userId", [new { userId = userId.ToString() }])
            .Should().Be(nameof(ExternalIdentityCapabilities.Login));
        Connection.ExecuteScalar<string>("SELECT external_identities FROM users WHERE id = @id", [new { id = userId.ToString() }]).Should().Be("[]");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Email));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenIdentityBelongsToDeletedUserInOtherTenant_ShouldLoginByEmailAndLinkIdentity()
    {
        // Arrange
        var otherTenantId = InsertTenant();
        var deletedUserId = InsertDeletedUser(Faker.Internet.Email(), otherTenantId);
        InsertExternalIdentity(deletedUserId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, otherTenantId);
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(userId.ToString());
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, otherTenantId).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, otherTenantId).Should().Be(deletedUserId.ToString());

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Email));
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenRecycledUserHoldsIdentityInSameTenant_ShouldMoveIdentityToLiveUser()
    {
        // Arrange
        var deletedUserId = InsertDeletedUser(Faker.Internet.Email());
        InsertExternalIdentity(deletedUserId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(userId.ToString());

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Email));
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenInvitedUserHasNoName_ShouldUpdateNameFromGoogleProfile()
    {
        // Arrange
        var invitedUserId = InsertUser(MockOAuthProvider.MockEmail, emailConfirmed: false);
        Connection.Update("users", "id", invitedUserId.ToString(), [("first_name", null), ("last_name", null)]);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        var firstName = Connection.ExecuteScalar<string>(
            "SELECT first_name FROM users WHERE email = @email", [new { email = MockOAuthProvider.MockEmail }]
        );
        var lastName = Connection.ExecuteScalar<string>(
            "SELECT last_name FROM users WHERE email = @email", [new { email = MockOAuthProvider.MockEmail }]
        );
        firstName.Should().Be(MockOAuthProvider.MockFirstName);
        lastName.Should().Be(MockOAuthProvider.MockLastName);
        Connection.ExecuteScalar<long>("SELECT email_confirmed FROM users WHERE email = @email", [new { email = MockOAuthProvider.MockEmail }]).Should().Be(1);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenUserAlreadyHasName_ShouldNotOverwriteFromGoogleProfile()
    {
        // Arrange
        var existingFirstName = Faker.Name.FirstName();
        var existingLastName = Faker.Name.LastName();
        var existingUserId = InsertUser(MockOAuthProvider.MockEmail);
        Connection.Update("users", "id", existingUserId.ToString(), [("first_name", existingFirstName), ("last_name", existingLastName)]);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        var firstName = Connection.ExecuteScalar<string>(
            "SELECT first_name FROM users WHERE email = @email", [new { email = MockOAuthProvider.MockEmail }]
        );
        var lastName = Connection.ExecuteScalar<string>(
            "SELECT last_name FROM users WHERE email = @email", [new { email = MockOAuthProvider.MockEmail }]
        );
        firstName.Should().Be(existingFirstName);
        lastName.Should().Be(existingLastName);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenNoCookie_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl, _) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, []);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenDefaultReturnPath_ShouldRedirectToRoot()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenValid_ShouldMarkCompletedInDatabase()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        await CallCallback(callbackUrl, cookies);

        // Assert
        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.Success));
        Connection.ExecuteScalar<string>("SELECT email FROM external_logins WHERE id = @id", [new { id = externalLoginId }]).Should().Be(MockOAuthProvider.MockEmail);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenUserNotFound_ShouldMarkFailedInDatabase()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        await CallCallback(callbackUrl, cookies);

        // Assert
        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.UserNotFound));
    }

    [Fact]
    public async Task CompleteExternalLogin_WithValidPreferredTenant_ShouldLoginToPreferredTenant()
    {
        // Arrange
        var tenant2Id = InsertTenant();
        var user1Id = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(user1Id, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var user2Id = InsertUser(MockOAuthProvider.MockEmail, tenant2Id);
        InsertExternalIdentity(user2Id, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id);
        var (callbackUrl, cookies) = await StartLoginFlow(preferredTenantId: tenant2Id);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(user2Id).Should().Be(tenant2Id.Value);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(user2Id);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Identity));
    }

    [Fact]
    public async Task CompleteExternalLogin_WithIdentityInSeveralTenantsAndNoPreferredTenant_ShouldLoginToFirstUserById()
    {
        // Arrange
        var tenant2Id = InsertTenant();
        var user1Id = InsertUser(Faker.Internet.Email());
        InsertExternalIdentity(user1Id, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var user2Id = InsertUser(Faker.Internet.Email(), tenant2Id);
        InsertExternalIdentity(user2Id, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id);
        // Ids minted in the same millisecond have no creation order, so the expected first by id is computed
        var expectedUserId = string.CompareOrdinal(user1Id.Value, user2Id.Value) < 0 ? user1Id : user2Id;
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE user_id = @userId", [new { userId = expectedUserId.ToString() }]).Should().Be(1);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(expectedUserId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Identity));
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenTamperedState_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackWithTamperedState(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=invalid_request");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenFlowIdMismatch_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl1, _) = await StartLoginFlow();
        var (_, cookies2) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackWithCrossedFlows(callbackUrl1, cookies2);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=authentication_failed");

        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl1);
        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.FlowIdMismatch));

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenTamperedCookie_ShouldRedirectToErrorPage()
    {
        // Arrange
        var (callbackUrl, _) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallbackWithTamperedCookie(callbackUrl, "corrupted-encrypted-data");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
    }

    [Fact]
    public async Task CompleteExternalLogin_WithInvalidPreferredTenant_ShouldLoginToDefaultTenant()
    {
        // Arrange
        var invalidTenantId = TenantId.NewId();
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow(preferredTenantId: invalidTenantId);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
    }

    [Fact]
    public async Task CompleteExternalLogin_WithPreferredTenantUserDoesNotHaveAccess_ShouldLoginToDefaultTenant()
    {
        // Arrange
        var tenant2Id = InsertTenant();
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        var (callbackUrl, cookies) = await StartLoginFlow(preferredTenantId: tenant2Id);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenIdentityInOtherTenantAndPreferredTenantUserMatchesByEmail_ShouldLoginToPreferredTenantAndLinkIdentity()
    {
        // Arrange
        var tenant2Id = InsertTenant();
        var user1Id = InsertUser(Faker.Internet.Email(), tenant2Id);
        InsertExternalIdentity(user1Id, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id);
        var user2Id = InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartLoginFlow(preferredTenantId: DatabaseSeeder.Tenant1.Id);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(user2Id).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(user2Id.ToString());
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id).Should().Be(user1Id.ToString());

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(user2Id);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Email));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenIdentityInOtherTenantAndPreferredTenantUserHasDifferentIdentity_ShouldRedirectToErrorPage()
    {
        // Arrange
        var tenant2Id = InsertTenant();
        var user1Id = InsertUser(Faker.Internet.Email(), tenant2Id);
        InsertExternalIdentity(user1Id, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id);
        var user2Id = InsertUser(MockOAuthProvider.MockEmail);
        InsertExternalIdentity(user2Id, ExternalProviderType.Google, "different-provider-user-id");
        var (callbackUrl, cookies) = await StartLoginFlow(preferredTenantId: DatabaseSeeder.Tenant1.Id);
        var externalLoginId = GetExternalLoginIdFromUrl(callbackUrl);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/error?error=authentication_failed");

        var loginResult = Connection.ExecuteScalar<string>(
            "SELECT login_result FROM external_logins WHERE id = @id", [new { id = externalLoginId }]
        );
        loginResult.Should().Be(nameof(ExternalLoginResult.IdentityMismatch));
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(0);
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id).Should().Be(1);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE user_id = @userId", [new { userId = user2Id.ToString() }]).Should().Be(0);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("ExternalLoginFailed");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenProfileHasNoEmailAndPreferredTenantHasNoIdentity_ShouldLoginToIdentityTenant()
    {
        // Arrange
        var tenant2Id = InsertTenant();
        var user1Id = InsertUser(Faker.Internet.Email(), tenant2Id);
        InsertExternalIdentity(user1Id, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId, tenant2Id);
        InsertUser(MockOAuthProvider.MockEmail);
        var (callbackUrl, cookies) = await StartLoginFlow(preferredTenantId: DatabaseSeeder.Tenant1.Id);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: MockOAuthProvider.NoEmailValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(user1Id).Should().Be(tenant2Id.Value);
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(0);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(user1Id);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Identity));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenAnotherLiveUserInTheTenantHoldsTheIdentityForVerificationOnly_ShouldRedirectToError()
    {
        // A verification-only row is invisible to the login lookup but still occupies the unique index on provider,
        // provider user id and tenant. Before this was handled the insert below failed in the database and surfaced as
        // a server error. Removing the row instead is not an option: it would destroy another person's verification
        // evidence as a side effect of an unrelated login.

        // Arrange
        var verifiedUserId = InsertUser(Faker.Internet.Email());
        var externalIdentityId = InsertExternalIdentity(verifiedUserId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        Connection.Update("external_identities", "id", externalIdentityId.ToString(), [("capabilities", nameof(ExternalIdentityCapabilities.Verification))]);

        const string emailPrefix = "another-person";
        var loggingInUserId = InsertUser($"{emailPrefix}{OAuthProviderFactory.MockEmailDomain}");
        var mockProviderCookieValue = $"identity:user-id-12345:{emailPrefix}";
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies, mockProviderCookieValue: mockProviderCookieValue);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/error?error=identity_already_linked");

        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE user_id = @userId", [new { userId = loggingInUserId.ToString() }]).Should().Be(0);

        // The verification evidence of the other user survives untouched
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(verifiedUserId.ToString());
        Connection.ExecuteScalar<string>("SELECT capabilities FROM external_identities WHERE id = @id", [new { id = externalIdentityId.ToString() }])
            .Should().Be(nameof(ExternalIdentityCapabilities.Verification));
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenIdentityHasNoLoginCapabilityAndEmailMatches_ShouldAddLoginCapabilityAndLogin()
    {
        // Arrange
        var userId = InsertUser(MockOAuthProvider.MockEmail);
        var externalIdentityId = InsertExternalIdentity(userId, ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId);
        Connection.Update("external_identities", "id", externalIdentityId.ToString(), [("capabilities", nameof(ExternalIdentityCapabilities.Verification))]);
        var (callbackUrl, cookies) = await StartLoginFlow();
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await CallCallback(callbackUrl, cookies);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");

        GetSessionTenantId(userId).Should().Be(DatabaseSeeder.Tenant1.Id.Value);
        CountExternalIdentities(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(1);
        GetExternalIdentityUserId(ExternalProviderType.Google, MockOAuthProvider.MockProviderUserId).Should().Be(userId.ToString());
        Connection.ExecuteScalar<string>("SELECT capabilities FROM external_identities WHERE id = @id", [new { id = externalIdentityId.ToString() }])
            .Should().Be($"{nameof(ExternalIdentityCapabilities.Login)}, {nameof(ExternalIdentityCapabilities.Verification)}");

        // The surviving row must be the original one, because a delete followed by an insert would be a replace
        // rather than the upgrade this test is named for. Keyed on the unique index rather than on user_id, so it
        // holds even if the capabilities assertion above is ever loosened to key on user_id.
        Connection.ExecuteScalar<string>(
            "SELECT id FROM external_identities WHERE provider = @provider AND provider_user_id = @providerUserId AND tenant_id = @tenantId",
            [new { provider = nameof(ExternalProviderType.Google), providerUserId = MockOAuthProvider.MockProviderUserId, tenantId = DatabaseSeeder.Tenant1.Id.Value }]
        ).Should().Be(externalIdentityId.ToString());

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(2);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("SessionCreated");
        TelemetryEventsCollectorSpy.CollectedEvents[1].GetType().Name.Should().Be("ExternalLoginCompleted");
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.user_id"].Should().Be(userId);
        TelemetryEventsCollectorSpy.CollectedEvents[1].Properties["event.lookup"].Should().Be(nameof(ExternalLoginLookup.Email));
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }
}
