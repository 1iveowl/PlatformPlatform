using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using SharedKernel.Domain;
using Xunit;

namespace Account.Tests.ExternalAuthentication.Domain;

public sealed class ExternalIdentityTests
{
    [Fact]
    public void Create_WhenCalled_ShouldSetSuppliedIssuerAndSubjectAndLoginCapability()
    {
        // Arrange
        var tenantId = TenantId.NewId();
        var userId = UserId.NewId();

        // Act
        var externalIdentity = ExternalIdentity.Create(tenantId, userId, ExternalProviderType.Google, "google-user-id-123", "https://accounts.google.com", "google-user-id-123");

        // Assert
        externalIdentity.Id.Value.Should().StartWith("exid_");
        externalIdentity.TenantId.Should().Be(tenantId);
        externalIdentity.UserId.Should().Be(userId);
        externalIdentity.Provider.Should().Be(ExternalProviderType.Google);
        externalIdentity.ProviderUserId.Should().Be("google-user-id-123");
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login);
        externalIdentity.Issuer.Should().Be("https://accounts.google.com");
        externalIdentity.Subject.Should().Be("google-user-id-123");
    }

    [Fact]
    public void Create_WhenSubjectDiffersFromProviderUserId_ShouldStoreBothAsSupplied()
    {
        // Arrange
        var tenantId = TenantId.NewId();
        var userId = UserId.NewId();

        // Act
        var externalIdentity = ExternalIdentity.Create(tenantId, userId, ExternalProviderType.Google, "provider-user-id-123", "https://issuer.example.com", "pairwise-subject-456");

        // Assert
        externalIdentity.ProviderUserId.Should().Be("provider-user-id-123");
        externalIdentity.Issuer.Should().Be("https://issuer.example.com");
        externalIdentity.Subject.Should().Be("pairwise-subject-456");
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login);
    }

    [Fact]
    public void Create_WhenEntraValues_ShouldStoreDirectoryAndObjectKeyIssuerAndSubjectVerbatim()
    {
        // Arrange
        var tenantId = TenantId.NewId();
        var userId = UserId.NewId();
        const string directoryId = "22222222-2222-2222-2222-222222222222";
        const string objectId = "33333333-3333-3333-3333-333333333333";

        // Act
        var externalIdentity = ExternalIdentity.Create(
            tenantId, userId, ExternalProviderType.Entra, $"{directoryId}:{objectId}", $"https://login.microsoftonline.com/{directoryId}/v2.0", "pairwise-subject-value"
        );

        // Assert
        externalIdentity.Provider.Should().Be(ExternalProviderType.Entra);
        externalIdentity.ProviderUserId.Should().Be($"{directoryId}:{objectId}");
        externalIdentity.Issuer.Should().Be($"https://login.microsoftonline.com/{directoryId}/v2.0");
        externalIdentity.Subject.Should().Be("pairwise-subject-value");
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login);
    }

    [Fact]
    public void AddLoginCapability_WhenTheIdentityIsVerificationOnly_ShouldKeepTheVerificationCapability()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity();

        // Act
        externalIdentity.AddLoginCapability();

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login | ExternalIdentityCapabilities.Verification);
    }

    [Fact]
    public void AddLoginCapability_WhenTheCapabilityIsAlreadyPresent_ShouldLeaveCapabilitiesUnchanged()
    {
        // Arrange
        var externalIdentity = ExternalIdentity.Create(TenantId.NewId(), UserId.NewId(), ExternalProviderType.Google, "google-user-id-123", "https://accounts.google.com", "google-user-id-123");

        // Act
        externalIdentity.AddLoginCapability();

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login);
    }

    [Fact]
    public void CreateForVerification_WhenCalled_ShouldRecordTheEvidenceWithoutGrantingLogin()
    {
        // Arrange
        var verifiedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        var authenticatedAt = verifiedAt.AddSeconds(-20);
        var externalLoginId = ExternalLoginId.NewId();

        // Act
        var externalIdentity = ExternalIdentity.CreateForVerification(
            TenantId.NewId(), UserId.NewId(), ExternalProviderType.MitId, "mitid-person-identifier", "https://tenant.idura.broker", "broker-pseudonym",
            IdentityAssuranceLevel.Substantial, verifiedAt, authenticatedAt, externalLoginId
        );

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Verification);
        externalIdentity.Capabilities.HasFlag(ExternalIdentityCapabilities.Login).Should().BeFalse();
        externalIdentity.AssuranceLevel.Should().Be(IdentityAssuranceLevel.Substantial);
        externalIdentity.VerifiedAt.Should().Be(verifiedAt);
        externalIdentity.AuthenticatedAt.Should().Be(authenticatedAt);
        externalIdentity.VerifiedByExternalLoginId.Should().Be(externalLoginId);
    }

    [Fact]
    public void RecordVerification_WhenTheIdentityWasAlreadyVerified_ShouldReplaceTheEvidence()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity();
        var laterVerifiedAt = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var laterAuthenticatedAt = laterVerifiedAt.AddSeconds(-10);
        var laterExternalLoginId = ExternalLoginId.NewId();

        // Act
        externalIdentity.RecordVerification(IdentityAssuranceLevel.High, laterVerifiedAt, laterAuthenticatedAt, laterExternalLoginId);

        // Assert
        externalIdentity.AssuranceLevel.Should().Be(IdentityAssuranceLevel.High);
        externalIdentity.VerifiedAt.Should().Be(laterVerifiedAt);
        externalIdentity.AuthenticatedAt.Should().Be(laterAuthenticatedAt);
        externalIdentity.VerifiedByExternalLoginId.Should().Be(laterExternalLoginId);
    }

    [Fact]
    public void RecordVerification_WhenTheIdentityCanAlreadyLogIn_ShouldKeepTheLoginCapability()
    {
        // Arrange
        var externalIdentity = ExternalIdentity.Create(TenantId.NewId(), UserId.NewId(), ExternalProviderType.MitId, "mitid-person-identifier", "https://tenant.idura.broker", "broker-pseudonym");

        // Act
        externalIdentity.RecordVerification(IdentityAssuranceLevel.Substantial, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ExternalLoginId.NewId());

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login | ExternalIdentityCapabilities.Verification);
    }

    [Fact]
    public void RevokeVerification_WhenTheIdentityIsVerified_ShouldClearTheEvidenceAndTheCapability()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity();

        // Act
        externalIdentity.RevokeVerification();

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.None);
        externalIdentity.AssuranceLevel.Should().BeNull();
        externalIdentity.VerifiedAt.Should().BeNull();
        externalIdentity.AuthenticatedAt.Should().BeNull();
        externalIdentity.VerifiedByExternalLoginId.Should().BeNull();
    }

    [Fact]
    public void RevokeVerification_WhenTheIdentityCanAlsoLogIn_ShouldKeepTheLoginCapability()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity();
        externalIdentity.AddLoginCapability();

        // Act
        externalIdentity.RevokeVerification();

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login);
    }

    private static ExternalIdentity CreateVerifiedIdentity()
    {
        return ExternalIdentity.CreateForVerification(
            TenantId.NewId(), UserId.NewId(), ExternalProviderType.MitId, "mitid-person-identifier", "https://tenant.idura.broker", "broker-pseudonym",
            IdentityAssuranceLevel.Substantial, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ExternalLoginId.NewId()
        );
    }
}
