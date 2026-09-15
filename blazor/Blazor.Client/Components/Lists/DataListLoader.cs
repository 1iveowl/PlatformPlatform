// Loads one page of a DataList through the page cache, with the recovery the account API's paging contract needs.
// The API rejects a page offset at or beyond its total number of pages with 400, and the same status also means other
// validation failures. So a 400 for a page after the first is checked against the first page's metadata (offset omitted):
// when the offset really is out of range, the last page is loaded instead (the first page when the result is empty);
// otherwise the original failure stands. At most three fetches per load, and never a loop.

namespace Blazor.Client.Components.Lists;

public sealed record DataListRequest(IReadOnlyDictionary<string, string> Filters, string OrderBy, SortOrder SortOrder, int PageOffset, int PageSize);

public sealed record DataListFetchResult<TItem>(DataListPage<TItem>? Page, int? StatusCode, string? ErrorMessage)
{
    public static DataListFetchResult<TItem> Success(IReadOnlyList<TItem> items, int totalCount)
    {
        return new DataListFetchResult<TItem>(new DataListPage<TItem>(items, totalCount), null, null);
    }

    public static DataListFetchResult<TItem> Failure(int? statusCode, string message)
    {
        return new DataListFetchResult<TItem>(null, statusCode, message);
    }
}

public delegate Task<DataListFetchResult<TItem>> DataListFetch<TItem>(DataListRequest request, CancellationToken cancellationToken);

public sealed record DataListLoadResult<TItem>(DataListPage<TItem>? Page, int PageOffset, string? ErrorMessage);

public static class DataListLoader
{
    public const int BadRequestStatusCode = 400;

    public static async Task<DataListLoadResult<TItem>> LoadAsync<TItem>(
        DataListPageCache cache,
        string scope,
        string listId,
        DataListState state,
        int pageSize,
        DataListFetch<TItem> fetch,
        CancellationToken cancellationToken)
    {
        var token = cache.BeginFetch(scope, listId);
        var queryKey = state.QueryKey;

        async Task<DataListFetchResult<TItem>> GetAsync(int pageOffset)
        {
            if (cache.TryGet<TItem>(scope, listId, queryKey, pageSize, pageOffset, out var cached)) return new DataListFetchResult<TItem>(cached, null, null);
            var request = new DataListRequest(state.Filters, state.OrderBy, state.SortOrder, pageOffset, pageSize);
            var result = await fetch(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Page is not null) cache.Store(token, listId, queryKey, pageSize, pageOffset, result.Page);
            return result;
        }

        var requested = await GetAsync(state.PageOffset);
        if (requested.Page is not null && (requested.Page.Items.Count > 0 || state.PageOffset == 0))
        {
            return new DataListLoadResult<TItem>(requested.Page, state.PageOffset, null);
        }

        var isOutOfRangeCandidate = state.PageOffset > 0 && (requested.Page is not null || requested.StatusCode == BadRequestStatusCode);
        if (!isOutOfRangeCandidate) return new DataListLoadResult<TItem>(null, state.PageOffset, requested.ErrorMessage);

        var first = await GetAsync(0);
        if (first.Page is null) return new DataListLoadResult<TItem>(null, state.PageOffset, first.ErrorMessage);

        var totalPages = (first.Page.TotalCount + pageSize - 1) / pageSize;
        if (state.PageOffset < totalPages)
        {
            // The offset is valid, so the failure was about something else
            return requested.Page is not null
                ? new DataListLoadResult<TItem>(requested.Page, state.PageOffset, null)
                : new DataListLoadResult<TItem>(null, state.PageOffset, requested.ErrorMessage);
        }

        if (totalPages <= 1) return new DataListLoadResult<TItem>(first.Page, 0, null);

        var last = await GetAsync(totalPages - 1);
        return last.Page is null
            ? new DataListLoadResult<TItem>(null, state.PageOffset, last.ErrorMessage)
            : new DataListLoadResult<TItem>(last.Page, totalPages - 1, null);
    }
}
