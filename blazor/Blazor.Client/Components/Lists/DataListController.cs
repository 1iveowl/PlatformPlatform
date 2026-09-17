// The behaviour of a DataList without its markup: the URL state, loading through the page cache, the selection and
// activation. The component renders it and forwards browser events to it.
//
// URL writes go through NavigationManager: a new history entry for page and sort changes, so Back and Forward restore
// them, and a replaced entry for filter and search changes, activation, closing and the out-of-range page correction.
// A state change never waits for the URL: the controller applies the new state at once and ignores the location change
// that its own navigation raises. When the host re-creates the component on navigation instead, the new instance reads
// the same state from the URL and the cache serves the page.
//
// Only the latest load may change the view. An older load that completes later, for an earlier search, sort or page, is
// discarded, and the cache refuses to store it when the list was invalidated in between.
//
// In the infinite load mode the rows are pages 0 to pageOffset in order, and pageOffset in the URL is the last loaded page,
// replaced rather than pushed as pages are appended. Restoring a range (a reload, Back or a copied URL) reads every page
// the cache still holds and fetches at most MaxRestoreFetches missing pages; when the budget runs out the range ends at
// the last page loaded, pageOffset is corrected, and the next page is one request away. Appending is one request at a
// time, rows already loaded are never repeated, a failed page keeps the rows and can be retried, and loading stops at
// the total count the server reports.

namespace Blazor.Client.Components.Lists;

public enum DataListLoadMode
{
    // One server page at a time with the paginator
    Pages,

    // Pages 0 to pageOffset appended in one scrolling list, the next page loaded on request
    Infinite
}

public enum DataListStatus
{
    Loading,
    Ready,
    Empty,
    Error
}

public sealed class DataListController<TItem>(
    DataListPageCache cache,
    DataListUrlOptions options,
    DataListSelectionMode selectionMode,
    Func<TItem, string> keySelector,
    Func<string> currentUri,
    Action<string, bool> navigate
) : IDisposable
{
    public const int PageSize = 25;

    public const int MaxRestoreFetches = 4;

    // The bulk endpoints accept at most this many ids, so select-all never selects more
    public const int MaxSelectedKeys = 100;

    private CancellationTokenSource? _loadCancellation;
    private string[] _pageKeys = [];
    private int _version;

    public required string ListId { get; set; }

    public required string CacheScope { get; set; }

    public required DataListFetch<TItem> Fetch { get; set; }

    public DataListUrlOptions Options { get; } = options;

    public DataListState State { get; private set; } = DataListState.Parse(currentUri(), options);

    public IReadOnlyList<TItem> Items { get; private set; } = [];

    public IReadOnlyList<string> PageKeys => _pageKeys;

    public int TotalCount { get; private set; }

    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;

    public DataListStatus Status { get; private set; } = DataListStatus.Loading;

    public string? ErrorMessage { get; private set; }

    public DataListLoadMode LoadMode { get; private set; } = DataListLoadMode.Pages;

    public bool IsLoadingNext { get; private set; }

    // The failure of the last attempt to append a page; the loaded rows stay and the next attempt retries
    public string? NextPageErrorMessage { get; private set; }

    public bool HasMore => LoadMode == DataListLoadMode.Infinite && Status == DataListStatus.Ready && State.PageOffset + 1 < TotalPages;

    public DataListSelection Selection { get; } = new(selectionMode);

    // Raised after a change of the selected keys; the second value is true when more than one row is selected
    public Func<IReadOnlySet<string>, bool, Task>? SelectionChanged { get; set; }

    public int ActiveIndex => State.SelectedKey is null ? -1 : Array.IndexOf(_pageKeys, State.SelectedKey);

    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    public async Task LoadAsync()
    {
        var version = ++_version;
        if (_loadCancellation is not null)
        {
            await _loadCancellation.CancelAsync();
            _loadCancellation.Dispose();
        }

        var cancellation = _loadCancellation = new CancellationTokenSource();
        var state = State;
        Status = DataListStatus.Loading;
        ErrorMessage = null;
        NextPageErrorMessage = null;
        IsLoadingNext = false;

        if (LoadMode == DataListLoadMode.Infinite)
        {
            await LoadRangeAsync(version, state, cancellation);
            return;
        }

        DataListLoadResult<TItem> result;
        try
        {
            result = await DataListLoader.LoadAsync(cache, CacheScope, ListId, state, PageSize, Fetch, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }

        if (version != _version) return;

        Items = result.Page?.Items ?? [];
        TotalCount = result.Page?.TotalCount ?? 0;
        _pageKeys = Items.Select(keySelector).ToArray();
        ErrorMessage = result.ErrorMessage;
        Status = result.ErrorMessage is not null ? DataListStatus.Error : Items.Count == 0 ? DataListStatus.Empty : DataListStatus.Ready;
        if (result.PageOffset != state.PageOffset) Navigate(State.WithPage(result.PageOffset), true);
    }

    // Switching keeps pageOffset and the activated row: the pages mode shows page pageOffset, the infinite mode pages 0 to
    // pageOffset. Selected rows that are no longer loaded are deselected.
    public async Task SetLoadModeAsync(DataListLoadMode loadMode)
    {
        if (loadMode == LoadMode) return;
        LoadMode = loadMode;
        await LoadAsync();
        var before = Selection.Keys.ToArray();
        if (Selection.Retain(_pageKeys)) await NotifyIfChangedAsync(before);
    }

    // Appends the page after the last loaded one; returns false when nothing was requested
    public async Task<bool> LoadNextAsync()
    {
        if (!HasMore || IsLoadingNext || _loadCancellation is null) return false;
        var version = _version;
        var cancellation = _loadCancellation;
        var state = State;
        var nextOffset = state.PageOffset + 1;
        IsLoadingNext = true;
        NextPageErrorMessage = null;

        DataListLoadResult<TItem> result;
        try
        {
            result = await DataListLoader.LoadAsync(cache, CacheScope, ListId, state.WithPage(nextOffset), PageSize, Fetch, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return true;
        }
        finally
        {
            if (version == _version) IsLoadingNext = false;
        }

        if (version != _version) return true;
        if (result.Page is null)
        {
            NextPageErrorMessage = result.ErrorMessage;
            return true;
        }

        TotalCount = result.Page.TotalCount;
        // A shorter list than before: the requested page no longer exists, so everything is loaded
        if (result.PageOffset != nextOffset) return true;
        Append(result.Page.Items);
        Navigate(State.WithPage(nextOffset), true);
        return true;
    }

    // Returns true when the view changed and needs a render
    public async Task<bool> LocationChangedAsync(string uri)
    {
        var next = DataListState.Parse(uri, Options);
        if (next.Equals(State)) return false;

        var rowsChanged = next.QueryKey != State.QueryKey || next.PageOffset != State.PageOffset;
        State = next;
        if (!rowsChanged) return true;

        await ClearSelectionAsync();
        await LoadAsync();
        return true;
    }

    // A click on the sorted column reverses its direction; another column sorts ascending
    public async Task SortAsync(string sortKey)
    {
        if (!Options.SortKeys.Contains(sortKey)) return;
        var sortOrder = State.OrderBy == sortKey && State.SortOrder == SortOrder.Ascending
            ? SortOrder.Descending
            : SortOrder.Ascending;
        Navigate(State.WithSort(sortKey, sortOrder), false);
        await ClearSelectionAsync();
        await LoadAsync();
    }

    public async Task GoToPageAsync(int pageOffset)
    {
        var target = Math.Clamp(pageOffset, 0, Math.Max(0, TotalPages - 1));
        if (target == State.PageOffset) return;
        Navigate(State.WithPage(target), false);
        await ClearSelectionAsync();
        await LoadAsync();
    }

    public async Task SetFiltersAsync(IReadOnlyDictionary<string, string?> changes)
    {
        var next = State.WithFilters(changes);
        if (next.QueryKey == State.QueryKey && next.PageOffset == State.PageOffset) return;
        Navigate(next, true);
        await ClearSelectionAsync();
        await LoadAsync();
    }

    // After a mutation: every cached variant of this list is dropped and the current page is loaded again
    public async Task InvalidateAsync()
    {
        cache.Invalidate(ListId);
        await ClearSelectionAsync();
        await LoadAsync();
    }

    // Returns the row to activate, or default when the click only changed the selection
    public async Task<(bool Activated, TItem? Item)> ClickAsync(int index, bool toggle, bool range)
    {
        if (index < 0 || index >= Items.Count) return (false, default);
        var before = Selection.Keys.ToArray();
        var activates = Selection.Click(_pageKeys, index, toggle, range);
        await NotifyIfChangedAsync(before);
        return activates ? (true, Activate(index)) : (false, default);
    }

    public async Task<(bool Activated, TItem? Item)> ActivateAsync(int index)
    {
        if (index < 0 || index >= Items.Count) return (false, default);
        if (Selection.Mode != DataListSelectionMode.None && !Selection.Keys.Contains(_pageKeys[index]))
        {
            var before = Selection.Keys.ToArray();
            Selection.Click(_pageKeys, index, false, false);
            await NotifyIfChangedAsync(before);
        }

        return (true, Activate(index));
    }

    public Task ToggleAsync(int index)
    {
        var before = Selection.Keys.ToArray();
        Selection.Toggle(_pageKeys, index);
        return NotifyIfChangedAsync(before);
    }

    public Task ExtendAsync(int fromIndex, int toIndex)
    {
        var before = Selection.Keys.ToArray();
        Selection.Extend(_pageKeys, fromIndex, toIndex);
        return NotifyIfChangedAsync(before);
    }

    public Task ToggleAllAsync()
    {
        var before = Selection.Keys.ToArray();
        Selection.ToggleAll(_pageKeys, MaxSelectedKeys);
        return NotifyIfChangedAsync(before);
    }

    // Returns true when a row was active
    public bool Close()
    {
        if (State.SelectedKey is null) return false;
        Navigate(State.WithSelectedKey(null), true);
        return true;
    }

    private async Task LoadRangeAsync(int version, DataListState state, CancellationTokenSource cancellation)
    {
        var loaded = new List<TItem>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var lastOffset = -1;
        var totalCount = 0;
        string? errorMessage = null;
        string? nextPageErrorMessage = null;
        var fetches = 0;
        try
        {
            for (var pageOffset = 0; pageOffset <= state.PageOffset; pageOffset++)
            {
                var pageState = state.WithPage(pageOffset);
                if (!cache.TryGet<TItem>(CacheScope, ListId, state.QueryKey, PageSize, pageOffset, out _))
                {
                    if (fetches == MaxRestoreFetches) break;
                    fetches++;
                }

                var result = await DataListLoader.LoadAsync(cache, CacheScope, ListId, pageState, PageSize, Fetch, cancellation.Token);
                if (result.Page is null)
                {
                    if (pageOffset == 0)
                    {
                        errorMessage = result.ErrorMessage;
                    }
                    else
                    {
                        nextPageErrorMessage = result.ErrorMessage;
                    }

                    break;
                }

                totalCount = result.Page.TotalCount;
                if (result.PageOffset != pageOffset) break;
                foreach (var item in result.Page.Items)
                {
                    if (keys.Add(keySelector(item))) loaded.Add(item);
                }

                lastOffset = pageOffset;
                if ((pageOffset + 1) * PageSize >= totalCount) break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }

        if (version != _version) return;

        Items = loaded;
        TotalCount = totalCount;
        _pageKeys = loaded.Select(keySelector).ToArray();
        ErrorMessage = errorMessage;
        NextPageErrorMessage = nextPageErrorMessage;
        Status = errorMessage is not null ? DataListStatus.Error : Items.Count == 0 ? DataListStatus.Empty : DataListStatus.Ready;
        var correctedOffset = Math.Max(0, lastOffset);
        if (correctedOffset != state.PageOffset) Navigate(State.WithPage(correctedOffset), true);
    }

    private void Append(IReadOnlyList<TItem> items)
    {
        var keys = new HashSet<string>(_pageKeys, StringComparer.Ordinal);
        var appended = Items.ToList();
        foreach (var item in items)
        {
            if (keys.Add(keySelector(item))) appended.Add(item);
        }

        Items = appended;
        _pageKeys = appended.Select(keySelector).ToArray();
    }

    private TItem Activate(int index)
    {
        if (Options.SelectedKeyName is not null && State.SelectedKey != _pageKeys[index]) Navigate(State.WithSelectedKey(_pageKeys[index]), true);
        return Items[index];
    }

    private void Navigate(DataListState next, bool replace)
    {
        State = next;
        var uri = next.ToUri(currentUri(), Options);
        if (uri != currentUri()) navigate(uri, replace);
    }

    private async Task ClearSelectionAsync()
    {
        if (Selection.Clear() && SelectionChanged is not null) await SelectionChanged(Selection.Keys, false);
    }

    private async Task NotifyIfChangedAsync(string[] before)
    {
        if (Selection.Keys.SetEquals(before) || SelectionChanged is null) return;
        await SelectionChanged(Selection.Keys, Selection.Keys.Count > 1);
    }
}
