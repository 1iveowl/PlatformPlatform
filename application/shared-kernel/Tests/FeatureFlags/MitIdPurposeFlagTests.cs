using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace SharedKernel.Tests.FeatureFlags;

/// <summary>
///     MitID serves more than one purpose from one set of credentials, so each purpose has to be enabled on its own
///     key. Keying either flag on the client id would make both true in exactly the same circumstances, which is the
///     failure these tests exist to catch.
/// </summary>
public sealed class MitIdPurposeFlagTests
{
    [Fact]
    public void IsSystemFeatureFlagEnabled_WhenOnlyVerificationIsEnabled_ShouldLeaveLoginDisabled()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
            {
                ["OAuth:MitId:Domain"] = "tenant.idura.broker",
                ["OAuth:MitId:ClientId"] = "urn:my:application:identifier:123",
                ["OAuth:MitId:ClientSecret"] = "a-secret",
                ["OAuth:MitId:VerificationEnabled"] = "true"
            }
        );

        // Act
        var isVerificationEnabled = FeatureFlagRegistry.MitIdVerification.IsSystemFeatureFlagEnabled(configuration);
        var isLoginEnabled = FeatureFlagRegistry.MitIdLogin.IsSystemFeatureFlagEnabled(configuration);

        // Assert
        isVerificationEnabled.Should().BeTrue();
        isLoginEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsSystemFeatureFlagEnabled_WhenOnlyLoginIsEnabled_ShouldLeaveVerificationDisabled()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
            {
                ["OAuth:MitId:ClientId"] = "urn:my:application:identifier:123",
                ["OAuth:MitId:LoginEnabled"] = "true"
            }
        );

        // Act
        var isVerificationEnabled = FeatureFlagRegistry.MitIdVerification.IsSystemFeatureFlagEnabled(configuration);
        var isLoginEnabled = FeatureFlagRegistry.MitIdLogin.IsSystemFeatureFlagEnabled(configuration);

        // Assert
        isVerificationEnabled.Should().BeFalse();
        isLoginEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsSystemFeatureFlagEnabled_WhenOnlyTheCredentialsAreConfigured_ShouldEnableNeitherPurpose()
    {
        // The migration this replaced: a client id alone used to enable verification. Configuration now has to say
        // what MitID is for, so credentials without a purpose offer nothing.

        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
            {
                ["OAuth:MitId:Domain"] = "tenant.idura.broker",
                ["OAuth:MitId:ClientId"] = "urn:my:application:identifier:123",
                ["OAuth:MitId:ClientSecret"] = "a-secret"
            }
        );

        // Act
        var isVerificationEnabled = FeatureFlagRegistry.MitIdVerification.IsSystemFeatureFlagEnabled(configuration);
        var isLoginEnabled = FeatureFlagRegistry.MitIdLogin.IsSystemFeatureFlagEnabled(configuration);

        // Assert
        isVerificationEnabled.Should().BeFalse();
        isLoginEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("")]
    public void IsSystemFeatureFlagEnabled_WhenTheValueIsNotExactlyTrue_ShouldStayDisabled(string value)
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["OAuth:MitId:LoginEnabled"] = value });

        // Act
        var isLoginEnabled = FeatureFlagRegistry.MitIdLogin.IsSystemFeatureFlagEnabled(configuration);

        // Assert
        isLoginEnabled.Should().BeFalse();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
