using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using SharedKernel.Domain;
using Xunit;

namespace Account.Tests.ExternalAuthentication.Domain;

public sealed class ExternalIdentityTests
{
    [Fact]
    public void Create_WhenProviderIsGoogle_ShouldSetLoginCapabilityAndDeriveIssuerAndSubject()
    {
        // Arrange
        var tenantId = TenantId.NewId();
        var userId = UserId.NewId();

        // Act
        var externalIdentity = ExternalIdentity.Create(tenantId, userId, ExternalProviderType.Google, "google-user-id-123");

        // Assert
        externalIdentity.Id.Value.Should().StartWith("exid_");
        externalIdentity.TenantId.Should().Be(tenantId);
        externalIdentity.UserId.Should().Be(userId);
        externalIdentity.Provider.Should().Be(ExternalProviderType.Google);
        externalIdentity.ProviderUserId.Should().Be("google-user-id-123");
        externalIdentity.Capabilities.Should().Be(ExternalIdentityCapabilities.Login);
        externalIdentity.Issuer.Should().Be("https://accounts.google.com");
        externalIdentity.Subject.Should().Be("google-user-id-123");
        externalIdentity.AssuranceLevel.Should().BeNull();
        externalIdentity.VerifiedAt.Should().BeNull();
        externalIdentity.EvidenceReference.Should().BeNull();
    }
}
