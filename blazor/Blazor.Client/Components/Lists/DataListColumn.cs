using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Blazor.Client.Components.Lists;

// A column the page declares. A column with a sort key renders a sort button in its header; the key is the value written to
// the URL and passed to the page's fetch delegate.
public sealed record DataListColumn<TItem>(string Title, RenderFragment<TItem> Cell, string? SortKey = null, string? Class = null);

// The texts DataList renders. English literals whose values are the resource keys until the shared localization
// resources exist; each text is used through this class only.
public static class DataListText
{
    public const string Actions = "Actions";
    public const string Empty = "No items found.";
    public const string Loading = "Loading";
    public const string NextPage = "Next page";
    public const string PageOf = "Page {0} of {1}";
    public const string Pagination = "Pagination";
    public const string PreviousPage = "Previous page";
    public const string SelectAll = "Select all rows on this page";
    public const string SelectRow = "Select {0}";
    public const string SortedAscending = "sorted ascending";
    public const string SortedDescending = "sorted descending";
    public const string TotalCount = "{0} total";

    public static string Format(string text, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, text, arguments);
    }
}
