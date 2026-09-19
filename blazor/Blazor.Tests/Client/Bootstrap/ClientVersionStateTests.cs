using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Bootstrap;

// What this runtime concludes about itself from an accepted bootstrap and from an asset the server no longer serves
public sealed class ClientVersionStateTests
{
    private const string ClientVersion = "1.0.0+9fd2c1a";

    [Fact]
    public void Apply_WhenServerVersionIsInTheWindow_ShouldBeSupportedAndNotStale()
    {
        // Arrange
        var state = new ClientVersionState(ClientVersion);

        // Act
        state.Apply(CreateBootstrap("1.0.4"));

        // Assert
        state.ServerVersion.Should().Be("1.0.4");
        state.Support.Should().Be(ClientVersionSupport.Supported);
        state.IsStale.Should().BeFalse();
    }

    [Fact]
    public void Apply_WhenServerVersionIsOutsideTheWindow_ShouldBeStale()
    {
        // Arrange
        var state = new ClientVersionState(ClientVersion);

        // Act
        state.Apply(CreateBootstrap("1.1.0"));

        // Assert
        state.Support.Should().Be(ClientVersionSupport.Unsupported);
        state.IsStale.Should().BeTrue();
    }

    [Fact]
    public void Apply_WhenTheBootstrapCarriesNoVersion_ShouldBeUnknownAndNotStale()
    {
        // Arrange
        var state = new ClientVersionState(ClientVersion);

        // Act
        state.Apply(CreateBootstrap(null));

        // Assert
        state.ServerVersion.Should().BeNull();
        state.Support.Should().Be(ClientVersionSupport.Unknown);
        state.IsStale.Should().BeFalse();
    }

    [Fact]
    public void Apply_WhenTheSessionEnds_ShouldForgetTheServerVersion()
    {
        // Arrange
        var state = new ClientVersionState(ClientVersion);
        state.Apply(CreateBootstrap("1.1.0"));

        // Act
        state.Apply(null);

        // Assert
        state.ServerVersion.Should().BeNull();
        state.Support.Should().Be(ClientVersionSupport.Unknown);
        state.IsStale.Should().BeFalse();
    }

    [Fact]
    public void ReportMissingAsset_WhenTheAssetIsFingerprinted_ShouldStayStaleThroughLaterBootstraps()
    {
        // Arrange
        var state = new ClientVersionState(ClientVersion);
        state.Apply(CreateBootstrap("1.0.0"));

        // Act
        state.ReportMissingAsset("/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm");
        state.ReportMissingAsset("/blazor/_framework/Account.Client.s3g968ygxj.wasm");
        state.Apply(CreateBootstrap("1.0.0"));

        // Assert
        state.Support.Should().Be(ClientVersionSupport.Supported);
        state.MissingAsset.Should().Be("/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm");
        state.IsStale.Should().BeTrue();
    }

    [Fact]
    public void ReportMissingAsset_WhenTheAssetIsNotFingerprinted_ShouldChangeNothing()
    {
        // Arrange
        var state = new ClientVersionState(ClientVersion);
        state.Apply(CreateBootstrap("1.0.0"));

        // Act
        state.ReportMissingAsset("/api/account/users/usr_01jz8q4n6v3k2m7p9r5t0w1xyz");

        // Assert
        state.MissingAsset.Should().BeNull();
        state.IsStale.Should().BeFalse();
    }

    private static BootstrapResponse CreateBootstrap(string? applicationVersion)
    {
        var runtimeConfiguration = new Dictionary<string, string>();
        if (applicationVersion is not null) runtimeConfiguration[BootstrapConfiguration.ApplicationVersionKey] = applicationVersion;

        var user = new BootstrapUser(new UserId("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ"), new TenantId(1), "Owner", "owner@example.com", null, null, null, null, null, null, null, false, []);
        return new BootstrapResponse(true, user, "en-US", runtimeConfiguration, new Dictionary<string, bool>(), "antiforgery-token");
    }
}
