using Blazor.Client.Bootstrap;
using FluentAssertions;

namespace Blazor.Tests.Client.Bootstrap;

// The supported version window and the fingerprint predicate behind the release policy in docs/blazor-version-policy.md
public sealed class ClientVersionWindowTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.9")]
    [InlineData("1.2.3+9fd2c1a", "1.2.3")]
    [InlineData("1.2.3-rc.1", "1.2.4+abc")]
    [InlineData("1.2", "1.2.0")]
    public void Evaluate_WhenMajorAndMinorMatch_ShouldBeSupported(string clientVersion, string serverVersion)
    {
        // Act
        var support = ClientVersionWindow.Evaluate(clientVersion, serverVersion);

        // Assert
        support.Should().Be(ClientVersionSupport.Supported);
    }

    [Theory]
    [InlineData("1.2.3", "1.3.0")]
    [InlineData("1.2.3", "2.2.3")]
    [InlineData("0.9.0", "1.0.0")]
    [InlineData("1.0.0+9fd2c1a", "1.1.0+9fd2c1a")]
    public void Evaluate_WhenMajorOrMinorDiffers_ShouldBeUnsupported(string clientVersion, string serverVersion)
    {
        // Act
        var support = ClientVersionWindow.Evaluate(clientVersion, serverVersion);

        // Assert
        support.Should().Be(ClientVersionSupport.Unsupported);
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("", "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData("1", "1.0.0")]
    [InlineData("one.two.three", "1.0.0")]
    [InlineData("1.0.0", "-1.0.0")]
    public void Evaluate_WhenAVersionCannotBeParsed_ShouldBeUnknown(string? clientVersion, string? serverVersion)
    {
        // Act
        var support = ClientVersionWindow.Evaluate(clientVersion, serverVersion);

        // Assert
        support.Should().Be(ClientVersionSupport.Unknown);
    }

    [Fact]
    public void CurrentClientVersion_ShouldBeAVersionTheWindowCanCompare()
    {
        // Act
        var support = ClientVersionWindow.Evaluate(ClientVersionWindow.CurrentClientVersion, ClientVersionWindow.CurrentClientVersion);

        // Assert
        ClientVersionWindow.CurrentClientVersion.Should().NotBeEmpty();
        support.Should().Be(ClientVersionSupport.Supported);
    }
}
