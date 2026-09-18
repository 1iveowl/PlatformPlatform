using Blazor.Client.Components;
using FluentAssertions;

namespace Blazor.Tests.Client.Components;

public sealed class SidePaneRulesTests
{
    [Theory]
    [InlineData(true, SidePaneMode.Docked)]
    [InlineData(false, SidePaneMode.FullScreen)]
    public void ModeFor_WhenTheMediumBreakpointIsReachedOrNot_ShouldDockOrFillTheScreen(bool reachesMedium, SidePaneMode expected)
    {
        // Arrange
        var matches = new ViewportMatches(true, reachesMedium, false, false, false);

        // Act
        var mode = SidePaneRules.ModeFor(matches);

        // Assert
        mode.Should().Be(expected);
    }

    [Fact]
    public void ModeFor_WhenTheServerRenders_ShouldDock()
    {
        // Act
        var mode = SidePaneRules.ModeFor(ViewportMatches.Widest);

        // Assert
        mode.Should().Be(SidePaneMode.Docked);
    }

    [Theory]
    [InlineData("https://localhost/blazor/account/users?userId=usr_1", "/blazor/account/users")]
    [InlineData("https://localhost/blazor/account/users?userId=usr_1#panel", "/blazor/account/users#panel")]
    [InlineData("https://localhost/blazor/account/users", "/blazor/account/users")]
    public void LocationKey_WhenUriGiven_ShouldBeThePathAndFragmentWithoutTheQuery(string uri, string expected)
    {
        // Act
        var key = SidePaneRules.LocationKey(uri);

        // Assert
        key.Should().Be(expected);
    }

    [Theory]
    // The activated row's key and the list state are query values, so writing them never closes the pane
    [InlineData("https://localhost/blazor/account/users?userId=usr_2&pageOffset=1", false)]
    [InlineData("https://localhost/blazor/account/users", false)]
    [InlineData("https://localhost/blazor/account/users/deleted?userId=usr_1", true)]
    [InlineData("https://localhost/blazor/app?userId=usr_1", true)]
    [InlineData("https://localhost/blazor/account/users?userId=usr_1#other", true)]
    public void ClosesOnNavigation_WhenTheLocationChanges_ShouldCloseOnlyOnAnotherPathOrFragment(string current, bool expected)
    {
        // Arrange
        var openedAt = SidePaneRules.LocationKey("https://localhost/blazor/account/users?userId=usr_1");

        // Act
        var closes = SidePaneRules.ClosesOnNavigation(openedAt, current);

        // Assert
        closes.Should().Be(expected);
    }
}
