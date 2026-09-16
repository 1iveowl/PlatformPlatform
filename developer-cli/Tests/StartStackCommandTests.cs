using DeveloperCli.Commands;
using FluentAssertions;
using SharedKernel.Configuration;

namespace DeveloperCli.Tests;

public sealed class StartStackCommandTests
{
    [Fact]
    public void BuildAppHostEnvironment_WhenWithoutBlazorHost_ShouldExcludeBlazorHostAndDisableDashboardAndProviders()
    {
        // Act
        var environment = StartStackCommand.BuildAppHostEnvironment(true);

        // Assert
        environment.Should().Equal(
            ("APPHOST_DISABLE_DASHBOARD", "true"),
            ("APPHOST_EXCLUDE_BLAZOR_HOST", "true"),
            ("Parameters__google-oauth-enabled", "false"),
            ("Parameters__entra-oauth-enabled", "false"),
            ("Parameters__mitid-oauth-enabled", "false"),
            ("Parameters__stripe-enabled", "false")
        );
    }

    [Fact]
    public void BuildAppHostEnvironment_WhenWithBlazorHost_ShouldKeepBlazorHost()
    {
        // Act
        var environment = StartStackCommand.BuildAppHostEnvironment(false);

        // Assert
        environment.Should().Contain(("APPHOST_EXCLUDE_BLAZOR_HOST", "false"));
        environment.Should().Contain(("APPHOST_DISABLE_DASHBOARD", "true"));
    }

    [Theory]
    [InlineData(9000, "platformplatform-postgres-fresh-data")]
    [InlineData(9100, "platformplatform-9100-postgres-fresh-data")]
    public void FreshPostgresDataVolumeName_ShouldNeverBeTheWorktreeDatabaseVolume(int basePort, string expectedVolumeName)
    {
        // Arrange
        var ports = new PortAllocation(basePort);

        // Act
        var volumeName = StartStackCommand.FreshPostgresDataVolumeName("platformplatform", ports);

        // Assert
        volumeName.Should().Be(expectedVolumeName);
        volumeName.Should().NotBe($"platformplatform{ports.VolumeNameInfix}-postgres-data");
    }
}
