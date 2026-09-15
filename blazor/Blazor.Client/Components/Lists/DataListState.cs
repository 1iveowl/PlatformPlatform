// The URL model of a DataList. The list's state lives only in the query string, so a deep link, a reload and Back or
// Forward restore it. The wrapper owns the sort, page and selected-row parameters; the page owns its filter parameters,
// which the wrapper carries without interpreting. Defaults are left out of the URL. QuickGrid's own sort, direction and
// page parameters are never written, and are dropped whenever the wrapper writes the URL.

using System.Globalization;
using System.Text;

namespace Blazor.Client.Components.Lists;

public sealed class DataListUrlOptions
{
    // The parameter names QuickGrid uses when its sort headers or paginator drive the URL
    public static readonly IReadOnlyList<string> QuickGridParameterNames = ["sort", "direction", "page"];

    public DataListUrlOptions(
        string defaultOrderBy,
        IReadOnlyCollection<string> sortKeys,
        IReadOnlyList<string>? filterNames = null,
        string? selectedKeyName = null,
        string parameterPrefix = "",
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? normalizeFilters = null)
    {
        if (!sortKeys.Contains(defaultOrderBy, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The default sort key '{defaultOrderBy}' is not one of the sort keys.", nameof(defaultOrderBy));
        }

        DefaultOrderBy = defaultOrderBy;
        SortKeys = sortKeys;
        FilterNames = filterNames ?? [];
        SelectedKeyName = string.IsNullOrEmpty(selectedKeyName) ? null : selectedKeyName;
        NormalizeFilters = normalizeFilters;
        OrderByName = Prefixed(parameterPrefix, "orderBy");
        SortOrderName = Prefixed(parameterPrefix, "sortOrder");
        PageOffsetName = Prefixed(parameterPrefix, "pageOffset");
    }

    public string DefaultOrderBy { get; }

    public IReadOnlyCollection<string> SortKeys { get; }

    public IReadOnlyList<string> FilterNames { get; }

    public string? SelectedKeyName { get; }

    public Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? NormalizeFilters { get; }

    public string OrderByName { get; }

    public string SortOrderName { get; }

    public string PageOffsetName { get; }

    internal IEnumerable<string> OwnedNames => FilterNames
        .Concat([OrderByName, SortOrderName, PageOffsetName])
        .Concat(SelectedKeyName is null ? [] : [SelectedKeyName]);

    private static string Prefixed(string prefix, string name)
    {
        return prefix.Length == 0 ? name : $"{prefix}{char.ToUpperInvariant(name[0])}{name[1..]}";
    }
}

public sealed record DataListState
{
    public DataListState(string orderBy, SortOrder sortOrder, int pageOffset, string? selectedKey, IReadOnlyDictionary<string, string>? filters = null)
    {
        OrderBy = orderBy;
        SortOrder = sortOrder;
        PageOffset = Math.Max(0, pageOffset);
        SelectedKey = string.IsNullOrEmpty(selectedKey) ? null : selectedKey;
        Filters = new SortedDictionary<string, string>(
            (filters ?? new Dictionary<string, string>()).Where(filter => !string.IsNullOrEmpty(filter.Value)).ToDictionary(),
            StringComparer.Ordinal
        );
    }

    public string OrderBy { get; init; }

    public SortOrder SortOrder { get; init; }

    public int PageOffset { get; init; }

    public string? SelectedKey { get; init; }

    public IReadOnlyDictionary<string, string> Filters { get; }

    // Everything that selects the rows of a page, except the page offset: the cache key's query part
    public string QueryKey
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var (name, value) in Filters)
            {
                builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value)).Append('&');
            }

            return builder.Append("orderBy=").Append(Uri.EscapeDataString(OrderBy)).Append("&sortOrder=").Append(SortOrder).ToString();
        }
    }

    public bool Equals(DataListState? other)
    {
        return other is not null && QueryKey == other.QueryKey && PageOffset == other.PageOffset && SelectedKey == other.SelectedKey;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(QueryKey, PageOffset, SelectedKey);
    }

    // A new sort starts on the first page
    public DataListState WithSort(string orderBy, SortOrder sortOrder)
    {
        return this with { OrderBy = orderBy, SortOrder = sortOrder, PageOffset = 0 };
    }

    public DataListState WithPage(int pageOffset)
    {
        return this with { PageOffset = Math.Max(0, pageOffset) };
    }

    // Filter and search changes start on the first page. A null or empty value removes the filter.
    public DataListState WithFilters(IReadOnlyDictionary<string, string?> changes)
    {
        var filters = Filters.ToDictionary(StringComparer.Ordinal);
        foreach (var (name, value) in changes)
        {
            if (string.IsNullOrEmpty(value))
            {
                filters.Remove(name);
            }
            else
            {
                filters[name] = value;
            }
        }

        return new DataListState(OrderBy, SortOrder, 0, SelectedKey, filters);
    }

    public DataListState WithSelectedKey(string? selectedKey)
    {
        return this with { SelectedKey = string.IsNullOrEmpty(selectedKey) ? null : selectedKey };
    }

    // Malformed, negative and unknown values fall back to their defaults instead of throwing
    public static DataListState Parse(string uri, DataListUrlOptions options)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in DataListQueryString.Decode(new Uri(uri).Query))
        {
            parameters[name] = value;
        }

        string? Value(string name)
        {
            return parameters.TryGetValue(name, out var value) && value.Length > 0 ? value : null;
        }

        var orderBy = options.SortKeys.FirstOrDefault(key => string.Equals(key, Value(options.OrderByName), StringComparison.OrdinalIgnoreCase)) ?? options.DefaultOrderBy;
        var sortOrder = string.Equals(Value(options.SortOrderName), nameof(SortOrder.Descending), StringComparison.OrdinalIgnoreCase) ? SortOrder.Descending : SortOrder.Ascending;
        var pageOffset = int.TryParse(Value(options.PageOffsetName), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedOffset) ? parsedOffset : 0;
        var filters = options.FilterNames.Select(name => (name, value: Value(name))).Where(filter => filter.value is not null).ToDictionary(filter => filter.name, filter => filter.value!, StringComparer.Ordinal);
        var normalizedFilters = options.NormalizeFilters is null ? filters : options.NormalizeFilters(filters);
        var selectedKey = options.SelectedKeyName is null ? null : Value(options.SelectedKeyName);
        return new DataListState(orderBy, sortOrder, pageOffset, selectedKey, normalizedFilters);
    }

    // Keeps the path, the fragment and every parameter the list does not own, in their original order and encoding
    public string ToUri(string currentUri, DataListUrlOptions options)
    {
        var uri = new Uri(currentUri);
        var owned = options.OwnedNames.Concat(DataListUrlOptions.QuickGridParameterNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = DataListQueryString.Split(uri.Query).Where(pair => !owned.Contains(pair.Name)).Select(pair => pair.Raw);

        var written = new List<string>();
        foreach (var name in options.FilterNames)
        {
            if (Filters.TryGetValue(name, out var value)) written.Add(DataListQueryString.Encode(name, value));
        }

        if (OrderBy != options.DefaultOrderBy) written.Add(DataListQueryString.Encode(options.OrderByName, OrderBy));
        if (SortOrder != SortOrder.Ascending) written.Add(DataListQueryString.Encode(options.SortOrderName, SortOrder.ToString()));
        if (PageOffset > 0) written.Add(DataListQueryString.Encode(options.PageOffsetName, PageOffset.ToString(CultureInfo.InvariantCulture)));
        if (options.SelectedKeyName is not null && SelectedKey is not null) written.Add(DataListQueryString.Encode(options.SelectedKeyName, SelectedKey));

        var query = string.Join('&', kept.Concat(written));
        return $"{uri.GetLeftPart(UriPartial.Path)}{(query.Length == 0 ? "" : $"?{query}")}{uri.Fragment}";
    }
}

internal static class DataListQueryString
{
    public static IEnumerable<(string Name, string Raw)> Split(string query)
    {
        return query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => (DecodeComponent(pair.Split('=', 2)[0]), pair));
    }

    public static IEnumerable<(string Name, string Value)> Decode(string query)
    {
        return query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => (DecodeComponent(parts[0]), DecodeComponent(parts.ElementAtOrDefault(1) ?? "")));
    }

    public static string Encode(string name, string value)
    {
        return $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }

    private static string DecodeComponent(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}
