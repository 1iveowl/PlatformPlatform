// Spike code (Blazor edition, stage B3): what the two grid components share, so the comparison differs only in the grid.
// The surface owns the list state, selection, dialogs and side pane; a grid renders rows, reports sort and page changes
// and fetches through UsersApiClient.

using Microsoft.AspNetCore.Components;

namespace Blazor.Client.Users;

public sealed record UsersGridStatus(bool IsLoading, int TotalCount, int PageIndex, string? Error = null);

public abstract class UsersGridBase : ComponentBase
{
    private readonly Dictionary<string, int> _indexById = new();
    private readonly Dictionary<string, UserDetails> _loadedByEmail = new();

    [Inject]
    protected UsersApiClient UsersApi { get; set; } = null!;

    [Parameter]
    [EditorRequired]
    public UsersListState State { get; set; } = null!;

    [Parameter]
    [EditorRequired]
    public IReadOnlySet<string> SelectedIds { get; set; } = null!;

    [Parameter]
    public string? CurrentUserId { get; set; }

    [Parameter]
    public bool IsOwner { get; set; }

    [Parameter]
    public EventCallback<UserDetails> OnOpenProfile { get; set; }

    [Parameter]
    public EventCallback<(UserDetails User, bool Selected)> OnSelectionToggled { get; set; }

    [Parameter]
    public EventCallback<UserDetails> OnChangeRole { get; set; }

    [Parameter]
    public EventCallback<UserDetails> OnDelete { get; set; }

    // The next three are plain delegates, not EventCallback, and the surface must not re-render until the ItemsProvider call
    // has returned. A re-render sets the grid's parameters mid-call; FluentDataGrid 5.0.0-preview.26254.1 then cancels the call
    // and never requests the range again, so the grid stays empty (observed in Chromium with 10,001 users). QuickGrid 11.0.0-rc.1
    // tolerates the same re-render.
    [Parameter]
    public Func<(SortableUserProperties OrderBy, SortOrder SortOrder), Task>? OnSortChanged { get; set; }

    [Parameter]
    public Func<int, Task>? OnPageChanged { get; set; }

    [Parameter]
    public Func<UsersGridStatus, Task>? OnStatusChanged { get; set; }

    protected bool IsVirtual => State.Mode == UsersListMode.Virtual;

    public abstract Task RefreshAsync();

    public abstract Task ScrollToIndexAsync(int index);

    public int? IndexOf(string userId)
    {
        return _indexById.TryGetValue(userId, out var index) ? index : null;
    }

    public UserDetails? FindLoaded(string email)
    {
        return _loadedByEmail.GetValueOrDefault(email);
    }

    protected int RowIndex(UserDetails user)
    {
        return _indexById.GetValueOrDefault(user.Id, -1);
    }

    protected static SortableUserProperties? ToSortable(string? columnTitle)
    {
        return columnTitle switch
        {
            "Name" => SortableUserProperties.Name,
            "Email" => SortableUserProperties.Email,
            "Created" => SortableUserProperties.CreatedAt,
            "Last seen" => SortableUserProperties.LastSeenAt,
            "Role" => SortableUserProperties.Role,
            _ => null
        };
    }

    // Both grids call this from their ItemsProvider: sort comes from the grid's header state, filters from the surface
    protected async Task<(IReadOnlyList<UserDetails> Items, int TotalCount)> FetchAsync(
        int startIndex, int? count, string? sortColumnTitle, bool sortAscending, CancellationToken cancellationToken)
    {
        var orderBy = ToSortable(sortColumnTitle) ?? State.OrderBy;
        var sortOrder = sortColumnTitle is null ? State.SortOrder : sortAscending ? SortOrder.Ascending : SortOrder.Descending;
        var effective = State with { OrderBy = orderBy, SortOrder = sortOrder };
        if (orderBy != State.OrderBy || sortOrder != State.SortOrder)
        {
            if (OnSortChanged is not null) await OnSortChanged((orderBy, sortOrder));
        }

        await NotifyStatusAsync(new UsersGridStatus(true, -1, startIndex / UsersApiClient.PagedPageSize));
        try
        {
            return await FetchRowsAsync(effective, startIndex, count, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Both grids swallow provider exceptions into their own loading or error state, so the failure is surfaced here
            await NotifyStatusAsync(new UsersGridStatus(false, -1, startIndex / UsersApiClient.PagedPageSize, exception.Message));
            throw;
        }
    }

    private async Task<(IReadOnlyList<UserDetails> Items, int TotalCount)> FetchRowsAsync(UsersListState effective, int startIndex, int? count, CancellationToken cancellationToken)
    {
        IReadOnlyList<(UserDetails User, int Index)> rows;
        int total;
        if (IsVirtual)
        {
            (rows, total) = await UsersApi.GetRangeAsync(effective, startIndex, count, cancellationToken);
            // Index the whole fetched server page, not only the rendered range, so a deep link can find a row the grid has
            // not rendered yet and scroll to it
            var fetchPageOffset = startIndex / UsersApiClient.VirtualFetchPageSize;
            var fetchedPage = await UsersApi.GetPageAsync(effective, fetchPageOffset, UsersApiClient.VirtualFetchPageSize);
            rows = fetchedPage.Users.Select((user, position) => (user, fetchPageOffset * UsersApiClient.VirtualFetchPageSize + position))
                .Where(row => row.Item2 < startIndex || row.Item2 >= startIndex + (count ?? 0)).Concat(rows).ToArray();
        }
        else
        {
            var pageIndex = startIndex / UsersApiClient.PagedPageSize;
            var page = await UsersApi.GetPageAsync(effective, pageIndex, UsersApiClient.PagedPageSize).WaitAsync(cancellationToken);
            rows = page.Users.Select((user, position) => (user, startIndex + position)).ToArray();
            total = page.TotalCount;
            if (pageIndex != State.PageOffset && OnPageChanged is not null) await OnPageChanged(pageIndex);
        }

        foreach (var (user, index) in rows)
        {
            _indexById[user.Id] = index;
            _loadedByEmail[user.Email] = user;
        }

        await NotifyStatusAsync(new UsersGridStatus(false, total, startIndex / UsersApiClient.PagedPageSize));
        var end = startIndex + (count ?? int.MaxValue - startIndex);
        return (rows.Where(row => row.Index >= startIndex && row.Index < end).OrderBy(row => row.Index).Select(row => row.User).ToArray(), total);
    }

    private Task NotifyStatusAsync(UsersGridStatus status)
    {
        return OnStatusChanged is null ? Task.CompletedTask : OnStatusChanged(status);
    }

    protected void ClearIndex()
    {
        _indexById.Clear();
        _loadedByEmail.Clear();
    }
}
