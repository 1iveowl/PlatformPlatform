using Blazor.Client.Components;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Components;

public sealed class DataListColumnClassTests
{
    private static readonly RenderFragment<string> Cell = _ => _ => { };

    [Fact]
    public void For_WhenTheColumnDeclaresNothing_ShouldBeEmpty()
    {
        // Act
        var className = DataListColumnClass.For(new DataListColumn<string>("Name", Cell), null, SortOrder.Ascending);

        // Assert
        className.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Breakpoint.Small, "data-list-hide-below-sm")]
    [InlineData(Breakpoint.Medium, "data-list-hide-below-md")]
    [InlineData(Breakpoint.ExtraExtraLarge, "data-list-hide-below-2xl")]
    public void For_WhenTheColumnHidesBelowABreakpoint_ShouldCarryThatBreakpointsClass(Breakpoint breakpoint, string expected)
    {
        // Act
        var className = DataListColumnClass.For(new DataListColumn<string>("Created", Cell, HideBelow: breakpoint), null, SortOrder.Ascending);

        // Assert
        className.Should().Be(expected);
    }

    [Theory]
    [InlineData(SortOrder.Ascending, "role data-list-hide-below-sm data-list-sorted-ascending")]
    [InlineData(SortOrder.Descending, "role data-list-hide-below-sm data-list-sorted-descending")]
    public void For_WhenTheColumnIsSorted_ShouldKeepItsOwnClassAndTheHiddenOne(SortOrder sortOrder, string expected)
    {
        // Arrange
        var column = new DataListColumn<string>("Role", Cell, "Role", "role", Breakpoint.Small);

        // Act
        var className = DataListColumnClass.For(column, "Role", sortOrder);

        // Assert
        className.Should().Be(expected);
    }

    [Fact]
    public void For_WhenAnotherColumnIsSorted_ShouldNotCarryASortedClass()
    {
        // Arrange
        var column = new DataListColumn<string>("Role", Cell, "Role", HideBelow: Breakpoint.Small);

        // Act
        var className = DataListColumnClass.For(column, "Email", SortOrder.Descending);

        // Assert
        className.Should().Be("data-list-hide-below-sm");
    }
}
