using Microsoft.AspNetCore.Components;

namespace Blazor.Client.Components.Lists;

// A column the page declares. A column with a sort key renders a sort button in its header; the key is the value written to
// the URL and passed to the page's fetch delegate. A column with HideBelow carries the stylesheet's hide-below class on its
// header and on every cell, so the whole column is dropped under that breakpoint and the row shows what is left.
public sealed record DataListColumn<TItem>(string Title, RenderFragment<TItem> Cell, string? SortKey = null, string? Class = null, Breakpoint? HideBelow = null);

// The classes a column's header and cells carry: what the page declared, the breakpoint it is hidden below, and the sorted
// state QuickGrid never writes itself.
public static class DataListColumnClass
{
    public static string For<TItem>(DataListColumn<TItem> column, string? orderBy, SortOrder sortOrder)
    {
        var sorted = column.SortKey is not null && column.SortKey == orderBy
            ? sortOrder == SortOrder.Ascending ? " data-list-sorted-ascending" : " data-list-sorted-descending"
            : "";
        var hidden = column.HideBelow is { } breakpoint ? $" {Breakpoints.HideBelowClass(breakpoint)}" : "";
        return $"{column.Class}{hidden}{sorted}".Trim();
    }
}
