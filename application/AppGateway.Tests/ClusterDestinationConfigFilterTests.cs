using AppGateway.Filters;
using FluentAssertions;
using SharedKernel.Configuration;
using Xunit;

namespace AppGateway.Tests;

// Every cluster the gateway proxies to resolves its address the same way: the deployed URL from an environment variable
// Bicep sets, else the local development port. The Blazor host is the newest of them and the only one served under a path
// base, so its resolution is asserted here rather than only being implied by the route configuration.
public sealed class ClusterDestinationConfigFilterTests
{
    private const string BlazorHostUrlKey = "BLAZOR_HOST_URL";

    [Fact]
    public void ResolveClusterAddress_WhenTheBlazorHostUrlIsSet_ShouldUseIt()
    {
        // Arrange
        var previousValue = Environment.GetEnvironmentVariable(BlazorHostUrlKey);
        Environment.SetEnvironmentVariable(BlazorHostUrlKey, "https://blazor-host.internal.example.azurecontainerapps.io");

        try
        {
            // Act
            var address = ClusterDestinationConfigFilter.ResolveClusterAddress("blazor-host", new PortAllocation(9000));

            // Assert
            address.Should().Be("https://blazor-host.internal.example.azurecontainerapps.io");
        }
        finally
        {
            Environment.SetEnvironmentVariable(BlazorHostUrlKey, previousValue);
        }
    }

    [Fact]
    public void ResolveClusterAddress_WhenTheBlazorHostUrlIsNotSet_ShouldUseTheLocalDevelopmentPort()
    {
        // Arrange
        var previousValue = Environment.GetEnvironmentVariable(BlazorHostUrlKey);
        Environment.SetEnvironmentVariable(BlazorHostUrlKey, null);

        try
        {
            // Act
            var address = ClusterDestinationConfigFilter.ResolveClusterAddress("blazor-host", new PortAllocation(9000));

            // Assert
            address.Should().Be("https://localhost:9017");
        }
        finally
        {
            Environment.SetEnvironmentVariable(BlazorHostUrlKey, previousValue);
        }
    }
}
