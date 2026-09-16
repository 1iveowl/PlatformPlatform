using DeveloperCli.Commands;
using FluentAssertions;

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
}
