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
    public void AddCapability_WhenCapabilityIsMissing_ShouldAddItToTheExistingCapabilities()
    {
        // Arrange
        var externalIdentity = ExternalIdentity.Create(TenantId.NewId(), UserId.NewId(), ExternalProviderType.Google, "google-user-id-123", "https://accounts.google.com", "google-user-id-123");

        // Act
        externalIdentity.AddCapability(ExternalIdentityCapabilities.Verification);

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login | ExternalIdentityCapabilities.Verification);
    }

    [Fact]
    public void AddCapability_WhenCapabilityIsAlreadyPresent_ShouldLeaveCapabilitiesUnchanged()
    {
        // Arrange
        var externalIdentity = ExternalIdentity.Create(TenantId.NewId(), UserId.NewId(), ExternalProviderType.Google, "google-user-id-123", "https://accounts.google.com", "google-user-id-123");
        externalIdentity.AddCapability(ExternalIdentityCapabilities.Verification);

        // Act
        externalIdentity.AddCapability(ExternalIdentityCapabilities.Verification);
        externalIdentity.AddCapability(ExternalIdentityCapabilities.Login);

        // Assert
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login | ExternalIdentityCapabilities.Verification);
    }
}
