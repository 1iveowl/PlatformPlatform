using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using Xunit;

namespace Account.Tests.ExternalAuthentication.Domain;

public sealed class ExternalAuthenticationPolicyTests
{
    [Theory]
    [InlineData(ExternalProviderType.Google, ExternalLoginType.Login)]
    [InlineData(ExternalProviderType.Google, ExternalLoginType.Signup)]
    [InlineData(ExternalProviderType.Entra, ExternalLoginType.Login)]
    [InlineData(ExternalProviderType.Entra, ExternalLoginType.Signup)]
    [InlineData(ExternalProviderType.MitId, ExternalLoginType.Verification)]
    public void IsFlowSupported_WhenTheCombinationIsAllowed_ShouldReturnTrue(ExternalProviderType providerType, ExternalLoginType loginType)
    {
        // Act
        var isSupported = ExternalAuthenticationPolicy.IsFlowSupported(providerType, loginType);

        // Assert
        isSupported.Should().BeTrue();
    }

    [Theory]
    [InlineData(ExternalProviderType.Google, ExternalLoginType.Verification)]
    [InlineData(ExternalProviderType.Entra, ExternalLoginType.Verification)]
    [InlineData(ExternalProviderType.MitId, ExternalLoginType.Login)]
    [InlineData(ExternalProviderType.MitId, ExternalLoginType.Signup)]
    public void IsFlowSupported_WhenTheCombinationIsForbidden_ShouldReturnFalse(ExternalProviderType providerType, ExternalLoginType loginType)
    {
        // Act
        var isSupported = ExternalAuthenticationPolicy.IsFlowSupported(providerType, loginType);

        // Assert
        isSupported.Should().BeFalse();
    }

    [Fact]
    public void IsFlowSupported_WhenAProviderIsAdded_ShouldRefuseEveryFlowUntilThePolicyNamesIt()
    {
        // Arrange
        var unknownProviderType = (ExternalProviderType)int.MaxValue;

        // Act
        var supportedFlows = Enum.GetValues<ExternalLoginType>().Where(loginType => ExternalAuthenticationPolicy.IsFlowSupported(unknownProviderType, loginType));

        // Assert
        supportedFlows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExternalLoginType.Login, false)]
    [InlineData(ExternalLoginType.Signup, false)]
    [InlineData(ExternalLoginType.Verification, true)]
    public void RequiresAuthenticatedUser_WhenCalled_ShouldOnlyBindTheVerificationFlow(ExternalLoginType loginType, bool expected)
    {
        // Act
        var requiresAuthenticatedUser = ExternalAuthenticationPolicy.RequiresAuthenticatedUser(loginType);

        // Assert
        requiresAuthenticatedUser.Should().Be(expected);
    }
}
