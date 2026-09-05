using System.Security;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.Users.Domain;
using FluentAssertions;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Domain;
using Xunit;

namespace Account.Tests.ExternalAuthentication.Domain;

public sealed class ExternalLoginTests
{
    [Fact]
    public void Create_WhenCalledWithValidParameters_ShouldSetAllPropertiesCorrectly()
    {
        // Act
        var externalLogin = ExternalLogin.Create(
            ExternalLoginType.Login,
            ExternalProviderType.Google,
            "code-verifier-123",
            "nonce-abc",
            "browser-fingerprint-abc",
            false
        );

        // Assert
        externalLogin.Id.Should().NotBeNull();
        externalLogin.Type.Should().Be(ExternalLoginType.Login);
        externalLogin.ProviderType.Should().Be(ExternalProviderType.Google);
        externalLogin.Email.Should().BeNull();
        externalLogin.CodeVerifier.Should().Be("code-verifier-123");
        externalLogin.Nonce.Should().Be("nonce-abc");
        externalLogin.BrowserFingerprint.Should().Be("browser-fingerprint-abc");
        externalLogin.LoginResult.Should().BeNull();
        externalLogin.IsConsumed.Should().BeFalse();
    }

    [Fact]
    public void MarkCompleted_WhenNotYetConsumed_ShouldSetLoginResultToSuccessAndEmail()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();

        // Act
        externalLogin.MarkCompleted("user@example.com");

        // Assert
        externalLogin.Email.Should().Be("user@example.com");
        externalLogin.LoginResult.Should().Be(ExternalLoginResult.Success);
        externalLogin.IsConsumed.Should().BeTrue();
    }

    [Fact]
    public void MarkCompleted_WhenEmailIsNull_ShouldSetLoginResultToSuccessWithoutEmail()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();

        // Act
        externalLogin.MarkCompleted(null);

        // Assert
        externalLogin.Email.Should().BeNull();
        externalLogin.LoginResult.Should().Be(ExternalLoginResult.Success);
        externalLogin.IsConsumed.Should().BeTrue();
    }

    [Fact]
    public void MarkCompleted_WhenAlreadyCompleted_ShouldThrowUnreachableException()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        externalLogin.MarkCompleted("user@example.com");

        // Act
        var act = () => externalLogin.MarkCompleted("user@example.com");

        // Assert
        act.Should().Throw<UnreachableException>()
            .WithMessage("The external login has already been completed.");
    }

    [Fact]
    public void MarkCompleted_WhenAlreadyFailed_ShouldThrowUnreachableException()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        externalLogin.MarkFailed(ExternalLoginResult.CodeExchangeFailed);

        // Act
        var act = () => externalLogin.MarkCompleted("user@example.com");

        // Assert
        act.Should().Throw<UnreachableException>()
            .WithMessage("The external login has already been completed.");
    }

    [Fact]
    public void MarkFailed_WhenNotYetConsumed_ShouldSetLoginResultToFailureReason()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();

        // Act
        externalLogin.MarkFailed(ExternalLoginResult.CodeExchangeFailed);

        // Assert
        externalLogin.LoginResult.Should().Be(ExternalLoginResult.CodeExchangeFailed);
        externalLogin.IsConsumed.Should().BeTrue();
    }

    [Fact]
    public void MarkFailed_WhenCalledWithSuccess_ShouldThrowUnreachableException()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();

        // Act
        var act = () => externalLogin.MarkFailed(ExternalLoginResult.Success);

        // Assert
        act.Should().Throw<UnreachableException>()
            .WithMessage("Cannot mark a login as failed with a success result.");
    }

    [Fact]
    public void MarkFailed_WhenAlreadyCompleted_ShouldThrowUnreachableException()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        externalLogin.MarkCompleted("user@example.com");

        // Act
        var act = () => externalLogin.MarkFailed(ExternalLoginResult.LoginExpired);

        // Assert
        act.Should().Throw<UnreachableException>()
            .WithMessage("The external login has already been completed.");
    }

    [Fact]
    public void MarkFailed_WhenAlreadyFailed_ShouldThrowUnreachableException()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        externalLogin.MarkFailed(ExternalLoginResult.InvalidState);

        // Act
        var act = () => externalLogin.MarkFailed(ExternalLoginResult.LoginExpired);

        // Assert
        act.Should().Throw<UnreachableException>()
            .WithMessage("The external login has already been completed.");
    }

    [Fact]
    public void IsExpired_WhenWithinValidPeriod_ShouldReturnFalse()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        var now = externalLogin.CreatedAt.AddSeconds(ExternalLogin.ValidForSeconds - 1);

        // Act
        var isExpired = externalLogin.IsExpired(now);

        // Assert
        isExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_WhenExactlyAtExpiry_ShouldReturnFalse()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        var now = externalLogin.CreatedAt.AddSeconds(ExternalLogin.ValidForSeconds);

        // Act
        var isExpired = externalLogin.IsExpired(now);

        // Assert
        isExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_WhenPastValidPeriod_ShouldReturnTrue()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        var now = externalLogin.CreatedAt.AddSeconds(ExternalLogin.ValidForSeconds + 1);

        // Act
        var isExpired = externalLogin.IsExpired(now);

        // Assert
        isExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_WhenNowIsBeforeCreatedAt_ShouldThrowSecurityException()
    {
        // Arrange
        var externalLogin = CreateExternalLogin();
        var pastTime = externalLogin.CreatedAt.AddSeconds(-1);

        // Act
        var act = () => externalLogin.IsExpired(pastTime);

        // Assert
        act.Should().Throw<SecurityException>()
            .WithMessage("*CreatedAt in the future*");
    }

    [Fact]
    public void AddExternalIdentity_WhenNewProvider_ShouldAddIdentity()
    {
        // Arrange
        var user = User.Create(TenantId.NewId(), "user@example.com", UserRole.Member, true, "en-US", 0);

        // Act
        user.AddExternalIdentity(ExternalProviderType.Google, "google-user-id-123");

        // Assert
        user.ExternalIdentities.Should().HaveCount(1);
        user.ExternalIdentities[0].Provider.Should().Be(ExternalProviderType.Google);
        user.ExternalIdentities[0].ProviderUserId.Should().Be("google-user-id-123");
    }

    [Fact]
    public void AddExternalIdentity_WhenDuplicateProvider_ShouldThrowUnreachableException()
    {
        // Arrange
        var user = User.Create(TenantId.NewId(), "user@example.com", UserRole.Member, true, "en-US", 0);
        user.AddExternalIdentity(ExternalProviderType.Google, "google-user-id-123");

        // Act
        var act = () => user.AddExternalIdentity(ExternalProviderType.Google, "different-google-id");

        // Assert
        act.Should().Throw<UnreachableException>()
            .WithMessage("*already has an external identity*");
    }

    [Fact]
    public void GetExternalIdentity_WhenProviderExists_ShouldReturnIdentity()
    {
        // Arrange
        var user = User.Create(TenantId.NewId(), "user@example.com", UserRole.Member, true, "en-US", 0);
        user.AddExternalIdentity(ExternalProviderType.Google, "google-user-id-123");

        // Act
        var identity = user.GetExternalIdentity(ExternalProviderType.Google);

        // Assert
        identity.Should().NotBeNull();
        identity.Provider.Should().Be(ExternalProviderType.Google);
        identity.ProviderUserId.Should().Be("google-user-id-123");
    }

    [Fact]
    public void GetExternalIdentity_WhenProviderDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var user = User.Create(TenantId.NewId(), "user@example.com", UserRole.Member, true, "en-US", 0);

        // Act
        var identity = user.GetExternalIdentity(ExternalProviderType.Google);

        // Assert
        identity.Should().BeNull();
    }

    [Theory]
    [InlineData(ExternalProviderType.Google, ExternalLoginType.Verification)]
    [InlineData(ExternalProviderType.Entra, ExternalLoginType.Verification)]
    [InlineData(ExternalProviderType.MitId, ExternalLoginType.Signup)]
    public void Create_WhenProviderDoesNotSupportTheFlow_ShouldThrow(ExternalProviderType providerType, ExternalLoginType loginType)
    {
        // Act
        var action = () => ExternalLogin.Create(loginType, providerType, "code-verifier", "nonce-value", "browser-fingerprint", false, UserId.NewId(), TenantId.NewId());

        // Assert
        action.Should().Throw<UnreachableException>().WithMessage($"Provider '{providerType}' does not support the '{loginType}' flow.");
    }

    [Fact]
    public void RecordResolvedUser_WhenTheFlowStartedUnbound_ShouldRecordTheAccount()
    {
        // Arrange
        var externalLogin = ExternalLogin.Create(ExternalLoginType.Login, ExternalProviderType.MitId, "code-verifier", "nonce-value", "browser-fingerprint", false);
        var userId = UserId.NewId();
        var tenantId = TenantId.NewId();

        // Act
        externalLogin.RecordResolvedUser(userId, tenantId);

        // Assert
        externalLogin.UserId.Should().Be(userId);
        externalLogin.TenantId.Should().Be(tenantId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordResolvedUser_WhenTheFlowIsBoundToAnotherAccount_ShouldThrow(bool sameUser)
    {
        // A verification flow is bound to its user before the provider replies, so resolving to a different one would
        // mean account resolution had contradicted the binding the callback already checked.

        // Arrange
        var boundUserId = UserId.NewId();
        var externalLogin = ExternalLogin.Create(ExternalLoginType.Verification, ExternalProviderType.MitId, "code-verifier", "nonce-value", "browser-fingerprint", false, boundUserId, TenantId.NewId());

        // Act
        var otherUserId = sameUser ? boundUserId : UserId.NewId();
        var action = () => externalLogin.RecordResolvedUser(otherUserId, TenantId.NewId());

        // Assert
        action.Should().Throw<UnreachableException>().WithMessage("The external login cannot resolve to another account.");
    }

    [Fact]
    public void Create_WhenVerificationFlowIsNotBoundToAUser_ShouldThrow()
    {
        // Act
        var action = () => ExternalLogin.Create(ExternalLoginType.Verification, ExternalProviderType.MitId, "code-verifier", "nonce-value", "browser-fingerprint", false);

        // Assert
        action.Should().Throw<UnreachableException>().WithMessage("The 'Verification' flow must be bound to a user and a tenant.");
    }

    [Fact]
    public void Create_WhenVerificationFlowHasNoTenant_ShouldThrow()
    {
        // Act
        var action = () => ExternalLogin.Create(ExternalLoginType.Verification, ExternalProviderType.MitId, "code-verifier", "nonce-value", "browser-fingerprint", false, UserId.NewId());

        // Assert
        action.Should().Throw<UnreachableException>().WithMessage("The 'Verification' flow must be bound to a user and a tenant.");
    }

    [Theory]
    [InlineData(ExternalLoginType.Login)]
    [InlineData(ExternalLoginType.Signup)]
    public void Create_WhenAnUnboundFlowIsGivenAUser_ShouldThrow(ExternalLoginType loginType)
    {
        // Act
        var action = () => ExternalLogin.Create(loginType, ExternalProviderType.Google, "code-verifier", "nonce-value", "browser-fingerprint", false, UserId.NewId(), TenantId.NewId());

        // Assert
        action.Should().Throw<UnreachableException>().WithMessage($"The '{loginType}' flow must not be bound to a user.");
    }

    [Fact]
    public void Create_WhenVerificationFlowIsBound_ShouldRecordTheActorAndTheProviderSelection()
    {
        // Arrange
        var userId = UserId.NewId();
        var tenantId = TenantId.NewId();
        var sessionId = SessionId.NewId();

        // Act
        var externalLogin = ExternalLogin.Create(
            ExternalLoginType.Verification, ExternalProviderType.MitId, "code-verifier", "nonce-value", "browser-fingerprint", true, userId, tenantId, sessionId
        );

        // Assert
        externalLogin.Type.Should().Be(ExternalLoginType.Verification);
        externalLogin.ProviderType.Should().Be(ExternalProviderType.MitId);
        externalLogin.UserId.Should().Be(userId);
        externalLogin.TenantId.Should().Be(tenantId);
        externalLogin.SessionId.Should().Be(sessionId);
        externalLogin.UsedMockProvider.Should().BeTrue();
    }

    [Fact]
    public void Create_WhenLoginFlowIsCreated_ShouldLeaveTheActorUnset()
    {
        // Act
        var externalLogin = CreateExternalLogin();

        // Assert
        externalLogin.UserId.Should().BeNull();
        externalLogin.TenantId.Should().BeNull();
        externalLogin.SessionId.Should().BeNull();
        externalLogin.UsedMockProvider.Should().BeFalse();
    }

    private static ExternalLogin CreateExternalLogin()
    {
        return ExternalLogin.Create(
            ExternalLoginType.Login,
            ExternalProviderType.Google,
            "code-verifier",
            "nonce-value",
            "browser-fingerprint",
            false
        );
    }
}
