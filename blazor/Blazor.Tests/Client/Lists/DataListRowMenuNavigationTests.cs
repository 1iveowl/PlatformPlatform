using Blazor.Client.Components.Lists;
using FluentAssertions;

namespace Blazor.Tests.Client.Lists;

// Arrow keys, Home and End in a row menu move between enabled items and wrap around
public sealed class DataListRowMenuNavigationTests
{
    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 1, 2)]
    [InlineData(2, 1, 0)]
    [InlineData(0, -1, 2)]
    [InlineData(3, -1, 2)]
    public void NextEnabled_ShouldSkipDisabledItemsAndWrap(int from, int step, int expected)
    {
        // Arrange
        var items = new[] { CreateItem("View profile"), CreateItem("Change role", true), CreateItem("Delete") };

        // Act
        var index = DataListRowMenuNavigation.NextEnabled(items, from, step);

        // Assert
        index.Should().Be(expected);
    }

    [Fact]
    public void NextEnabled_WhenEveryItemIsDisabled_ShouldReturnMinusOne()
    {
        // Arrange
        var items = new[] { CreateItem("Change role", true) };

        // Act
        var index = DataListRowMenuNavigation.NextEnabled(items, -1, 1);

        // Assert
        index.Should().Be(-1);
    }

    private static DataListRowMenuItem CreateItem(string label, bool disabled = false)
    {
        return new DataListRowMenuItem(label, () => Task.CompletedTask, disabled);
    }
}
