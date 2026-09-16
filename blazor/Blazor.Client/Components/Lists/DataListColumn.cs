using Microsoft.AspNetCore.Components;

namespace Blazor.Client.Components.Lists;

// A column the page declares. A column with a sort key renders a sort button in its header; the key is the value written to
// the URL and passed to the page's fetch delegate.
public sealed record DataListColumn<TItem>(string Title, RenderFragment<TItem> Cell, string? SortKey = null, string? Class = null);
