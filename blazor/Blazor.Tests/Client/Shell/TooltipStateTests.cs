using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class TooltipStateTests
{
    [Theory]
    [InlineData("mouse")]
    [InlineData("pen")]
    public void PointerEntered_WhenHoveredAndLeft_ShouldOpenThenClose(string pointerType)
    {
        // Arrange
        var state = new TooltipState();

        // Act
        state.PointerEntered(pointerType);
        var openWhileHovered = state.IsOpen;
        state.PointerLeft(pointerType);

        // Assert
        openWhileHovered.Should().BeTrue();
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void PointerEntered_WhenTouch_ShouldNotOpen()
    {
        // Arrange
        var state = new TooltipState();

        // Act
        state.PointerEntered("touch");

        // Assert
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void FocusEntered_WhenFocusMovesInAndOut_ShouldOpenThenClose()
    {
        // Arrange
        var state = new TooltipState();

        // Act
        state.FocusEntered();
        var openWhileFocused = state.IsOpen;
        state.FocusLeft();

        // Assert
        openWhileFocused.Should().BeTrue();
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void PointerReleased_WhenTouchTappedTwice_ShouldToggle()
    {
        // Arrange
        var state = new TooltipState();

        // Act
        state.PointerReleased("touch");
        var openAfterFirstTap = state.IsOpen;
        state.PointerReleased("touch");

        // Assert
        openAfterFirstTap.Should().BeTrue();
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void PointerReleased_WhenSecondTapLandsOnTheFocusedControl_ShouldHideUntilFocusLeaves()
    {
        // Arrange
        var state = new TooltipState();
        state.FocusEntered();
        state.PointerReleased("touch");

        // Act
        state.PointerReleased("touch");
        var openAfterSecondTap = state.IsOpen;
        state.FocusLeft();
        state.FocusEntered();

        // Assert
        openAfterSecondTap.Should().BeFalse();
        state.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void PointerReleased_WhenMouse_ShouldNotToggle()
    {
        // Arrange
        var state = new TooltipState();

        // Act
        state.PointerReleased("mouse");

        // Assert
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void FocusLeft_WhenOpenedByTap_ShouldClose()
    {
        // Arrange
        var state = new TooltipState();
        state.PointerReleased("touch");

        // Act
        state.FocusLeft();

        // Assert
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void KeyPressed_WhenEscapeWhileFocused_ShouldStayHiddenUntilFocusReturns()
    {
        // Arrange
        var state = new TooltipState();
        state.FocusEntered();

        // Act
        state.KeyPressed("Escape");
        var openAfterEscape = state.IsOpen;
        state.PointerEntered("mouse");
        var openWhenHoveredAfterEscape = state.IsOpen;
        state.PointerLeft("mouse");
        state.FocusLeft();
        state.FocusEntered();

        // Assert
        openAfterEscape.Should().BeFalse();
        openWhenHoveredAfterEscape.Should().BeFalse();
        state.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void KeyPressed_WhenOtherKey_ShouldKeepOpen()
    {
        // Arrange
        var state = new TooltipState();
        state.FocusEntered();

        // Act
        state.KeyPressed("Enter");

        // Assert
        state.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void PointerReleased_WhenTappedAfterEscape_ShouldReopen()
    {
        // Arrange
        var state = new TooltipState();
        state.PointerReleased("touch");
        state.KeyPressed("Escape");

        // Act
        state.PointerReleased("touch");

        // Assert
        state.IsOpen.Should().BeTrue();
    }
}
