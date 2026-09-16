using Blazor.Client.Components.Lists;
using FluentAssertions;

namespace Blazor.Tests.Client.Lists;

public sealed class DataListSelectionTests
{
    private static readonly string[] PageKeys = ["a", "b", "c", "d", "e"];

    [Fact]
    public void Click_WhenPlain_ShouldSelectOnlyThatRowAndActivate()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.Multiple);
        selection.ToggleAll(PageKeys);

        // Act
        var activates = selection.Click(PageKeys, 2, false, false);

        // Assert
        activates.Should().BeTrue();
        selection.Keys.Should().BeEquivalentTo("c");
    }

    [Fact]
    public void Click_WhenToggling_ShouldAddAndRemoveWithoutActivating()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.Multiple);
        selection.Click(PageKeys, 0, false, false);

        // Act
        var firstToggle = selection.Click(PageKeys, 3, true, false);
        selection.Click(PageKeys, 0, true, false);

        // Assert
        firstToggle.Should().BeFalse();
        selection.Keys.Should().BeEquivalentTo("d");
    }

    [Fact]
    public void Click_WhenShiftClicking_ShouldSelectTheRangeFromTheAnchor()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.Multiple);
        selection.Click(PageKeys, 3, false, false);

        // Act
        var activates = selection.Click(PageKeys, 1, false, true);

        // Assert
        activates.Should().BeFalse();
        selection.Keys.Should().BeEquivalentTo("b", "c", "d");
        selection.AnchorIndex.Should().Be(3);
    }

    [Fact]
    public void Extend_WhenMovingWithShift_ShouldGrowAndShrinkFromTheAnchor()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.Multiple);

        // Act
        selection.Extend(PageKeys, 1, 2);
        selection.Extend(PageKeys, 2, 3);
        var grown = selection.Keys.ToArray();
        selection.Extend(PageKeys, 3, 0);

        // Assert
        grown.Should().BeEquivalentTo("b", "c", "d");
        selection.Keys.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void ToggleAll_ShouldSelectThePageThenClearIt()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.Multiple);
        selection.Toggle(PageKeys, 1);
        var partial = selection.GetHeaderSelection(PageKeys);

        // Act
        selection.ToggleAll(PageKeys);
        var all = selection.GetHeaderSelection(PageKeys);
        selection.ToggleAll(PageKeys);

        // Assert
        partial.Should().Be(DataListHeaderSelection.Some);
        all.Should().Be(DataListHeaderSelection.All);
        selection.GetHeaderSelection(PageKeys).Should().Be(DataListHeaderSelection.None);
        selection.Keys.Should().BeEmpty();
    }

    [Fact]
    public void Single_WhenTogglingOrExtending_ShouldKeepAtMostOneRow()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.Single);

        // Act
        selection.Toggle(PageKeys, 0);
        selection.Toggle(PageKeys, 2);
        selection.Extend(PageKeys, 2, 4);
        selection.ToggleAll(PageKeys);
        var rangeActivates = selection.Click(PageKeys, 4, false, true);

        // Assert
        rangeActivates.Should().BeTrue();
        selection.Keys.Should().BeEquivalentTo("e");
    }

    [Fact]
    public void None_ShouldNeverSelectAndActivateOnlyOnPlainClick()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.None);

        // Act
        var plain = selection.Click(PageKeys, 1, false, false);
        var toggle = selection.Click(PageKeys, 1, true, false);
        selection.Toggle(PageKeys, 1);

        // Assert
        plain.Should().BeTrue();
        toggle.Should().BeFalse();
        selection.Keys.Should().BeEmpty();
    }

    [Fact]
    public void Click_WhenIndexIsOutOfRange_ShouldChangeNothing()
    {
        // Arrange
        var selection = new DataListSelection(DataListSelectionMode.Multiple);
        selection.Toggle(PageKeys, 0);

        // Act
        var activates = selection.Click(PageKeys, 9, false, false);

        // Assert
        activates.Should().BeFalse();
        selection.Keys.Should().BeEquivalentTo("a");
    }
}
