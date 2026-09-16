using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class ShellSidebarStateTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("TRUE", false)]
    [InlineData("{\"collapsed\":true}", false)]
    public void IsCollapsed_WhenWide_ShouldFollowAWellFormedStoredValueElseExpand(string? storedValue, bool expected)
    {
        // Act
        var collapsed = ShellSidebarState.IsCollapsed(storedValue, true);

        // Assert
        collapsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    [InlineData("malformed")]
    public void IsCollapsed_WhenNarrow_ShouldBeCollapsed(string? storedValue)
    {
        // Act
        var collapsed = ShellSidebarState.IsCollapsed(storedValue, false);

        // Assert
        collapsed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FormatStored_WhenParsedBack_ShouldRoundTrip(bool collapsed)
    {
        // Act
        var restored = ShellSidebarState.IsCollapsed(ShellSidebarState.FormatStored(collapsed), true);

        // Assert
        restored.Should().Be(collapsed);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldPersist_WhenViewportWidthGiven_ShouldPersistOnlyOnWideViewports(bool isWide, bool expected)
    {
        // Act
        var persist = ShellSidebarState.ShouldPersist(isWide);

        // Assert
        persist.Should().Be(expected);
    }
}
