using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Shared;
using FluentAssertions;
using SharedKernel.Domain;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class RequireVerifiedIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaximumAge = TimeSpan.FromDays(30);

    [Fact]
    public void IsSatisfied_WhenTheVerificationIsFreshAndAtTheMinimumLevel_ShouldReturnTrue()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity(IdentityAssuranceLevel.Substantial, Now.AddDays(-1));

        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(externalIdentity, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeTrue();
    }

    [Fact]
    public void IsSatisfied_WhenTheVerificationIsAboveTheMinimumLevel_ShouldReturnTrue()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity(IdentityAssuranceLevel.High, Now.AddDays(-1));

        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(externalIdentity, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeTrue();
    }

    [Fact]
    public void IsSatisfied_WhenTheVerificationIsBelowTheMinimumLevel_ShouldReturnFalse()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity(IdentityAssuranceLevel.Low, Now.AddDays(-1));

        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(externalIdentity, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeFalse();
    }

    [Fact]
    public void IsSatisfied_WhenTheAuthenticationIsOlderThanTheMaximumAge_ShouldReturnFalse()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity(IdentityAssuranceLevel.Substantial, Now.AddDays(-31));

        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(externalIdentity, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeFalse();
    }

    [Fact]
    public void IsSatisfied_WhenTheRowIsRecentButTheAuthenticationIsNot_ShouldReturnFalse()
    {
        // Freshness is measured against when the person authenticated, not against when the row was written. A
        // provider replaying a cached session would otherwise make a years-old authentication look current.

        // Arrange
        var externalIdentity = ExternalIdentity.CreateForVerification(
            TenantId.NewId(), UserId.NewId(), ExternalProviderType.MitId, "mitid-person-identifier", "https://tenant.idura.broker", "broker-pseudonym",
            IdentityAssuranceLevel.Substantial, Now, Now.AddYears(-2), ExternalLoginId.NewId()
        );

        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(externalIdentity, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeFalse();
    }

    [Fact]
    public void IsSatisfied_WhenTheIdentityWasNeverVerified_ShouldReturnFalse()
    {
        // Arrange
        var externalIdentity = ExternalIdentity.Create(TenantId.NewId(), UserId.NewId(), ExternalProviderType.Google, "google-user-id", "https://accounts.google.com", "google-user-id");

        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(externalIdentity, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeFalse();
    }

    [Fact]
    public void IsSatisfied_WhenTheVerificationWasRevoked_ShouldReturnFalse()
    {
        // Arrange
        var externalIdentity = CreateVerifiedIdentity(IdentityAssuranceLevel.Substantial, Now.AddDays(-1));
        externalIdentity.RevokeVerification();

        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(externalIdentity, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeFalse();
    }

    [Fact]
    public void IsSatisfied_WhenThereIsNoIdentity_ShouldReturnFalse()
    {
        // Act
        var isSatisfied = RequireVerifiedIdentity.IsSatisfied(null, IdentityAssuranceLevel.Substantial, MaximumAge, Now);

        // Assert
        isSatisfied.Should().BeFalse();
    }

    private static ExternalIdentity CreateVerifiedIdentity(IdentityAssuranceLevel assuranceLevel, DateTimeOffset authenticatedAt)
    {
        return ExternalIdentity.CreateForVerification(
            TenantId.NewId(), UserId.NewId(), ExternalProviderType.MitId, "mitid-person-identifier", "https://tenant.idura.broker", "broker-pseudonym",
            assuranceLevel, authenticatedAt, authenticatedAt, ExternalLoginId.NewId()
        );
    }
}
