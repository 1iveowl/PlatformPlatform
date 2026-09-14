// Spike code (Blazor edition, stage B3): the users calls of the account API through the gateway, and the page cache that
// both grids read. The account API pages by PageOffset and PageSize (at most 1000); a virtualized grid asks for arbitrary
// index ranges, so ranges are served from fixed server pages that are fetched once per filter state. A transitional facade
// over the typed UsersClient until the shared list and cache foundation replaces it.

using Account.Client;
using Account.Features.Users.Requests;
using SharedKernel.Domain;

namespace Blazor.Client.Users;

public sealed class UsersApiException(int? statusCode, string message)
    : Exception($"The account API returned {statusCode?.ToString() ?? "no response"}: {message}")
{
    public int? StatusCode { get; } = statusCode;

    public static UsersApiException FromProblem(ApiCallOutcome outcome, ApiCallProblem problem)
    {
        var fieldErrors = problem.Errors.Values.SelectMany(messages => messages).ToArray();
        var message = fieldErrors.Length > 0 ? string.Join(" ", fieldErrors) : problem.Detail ?? problem.Title ?? outcome.ToString();
        return new UsersApiException(problem.StatusCode, message);
    }
}

public sealed class UsersApiClient(UsersClient usersClient)
{
    public const int VirtualFetchPageSize = 100;
    public const int PagedPageSize = 25;

    private readonly Dictionary<(GetUsersQuery Query, int PageSize, int PageOffset), Task<UsersResponse>> _pages = new();

    public int RequestCount { get; private set; }

    public void Invalidate()
    {
        _pages.Clear();
    }

    public Task<UsersResponse> GetPageAsync(UsersListState state, int pageOffset, int pageSize)
    {
        var key = (state.ToUsersQuery(), pageSize, pageOffset);
        if (_pages.TryGetValue(key, out var cached) && cached is { IsFaulted: false, IsCanceled: false }) return cached;

        var task = FetchPageAsync(key.Item1, pageOffset, pageSize);
        _pages[key] = task;
        return task;
    }

    // Serves [startIndex, startIndex + count) from fixed server pages. The HTTP call is not cancelled with the grid's token,
    // so a page that a scroll abandons still lands in the cache for the next request.
    public async Task<(IReadOnlyList<(UserDetails User, int Index)> Items, int TotalCount)> GetRangeAsync(
        UsersListState state,
        int startIndex,
        int? count,
        CancellationToken cancellationToken)
    {
        var first = await GetPageAsync(state, startIndex / VirtualFetchPageSize, VirtualFetchPageSize).WaitAsync(cancellationToken);
        var total = first.TotalCount;
        var end = Math.Min(total, startIndex + (count ?? VirtualFetchPageSize));
        var items = new List<(UserDetails, int)>();
        for (var pageOffset = startIndex / VirtualFetchPageSize; pageOffset * VirtualFetchPageSize < end; pageOffset++)
        {
            var page = pageOffset == startIndex / VirtualFetchPageSize ? first : await GetPageAsync(state, pageOffset, VirtualFetchPageSize).WaitAsync(cancellationToken);
            for (var position = 0; position < page.Users.Length; position++)
            {
                var index = pageOffset * VirtualFetchPageSize + position;
                if (index >= startIndex && index < end) items.Add((page.Users[position], index));
            }
        }

        return (items, total);
    }

    public async Task<UserDetails> GetUserAsync(string id)
    {
        return EnsureSuccess(await usersClient.GetUserAsync(new UserId(id), CancellationToken.None));
    }

    public async Task ChangeRoleAsync(string id, UserRole role)
    {
        EnsureSuccess(await usersClient.ChangeUserRoleAsync(new UserId(id), new ChangeUserRoleCommand { UserRole = role }, CancellationToken.None));
    }

    public async Task DeleteAsync(string id)
    {
        EnsureSuccess(await usersClient.DeleteUserAsync(new UserId(id), CancellationToken.None));
    }

    public async Task BulkDeleteAsync(IEnumerable<string> ids)
    {
        EnsureSuccess(await usersClient.BulkDeleteUsersAsync(new BulkDeleteUsersCommand(ids.Select(id => new UserId(id)).ToArray()), CancellationToken.None));
    }

    private async Task<UsersResponse> FetchPageAsync(GetUsersQuery query, int pageOffset, int pageSize)
    {
        RequestCount++;
        // The API rejects a PageOffset at or beyond the last page, which includes offset 0 of an empty result, so page 0 omits it
        var pageQuery = query with { PageSize = pageSize, PageOffset = pageOffset == 0 ? null : pageOffset };
        return EnsureSuccess(await usersClient.GetUsersAsync(pageQuery, CancellationToken.None));
    }

    private static TValue EnsureSuccess<TValue>(ApiCallResult<TValue> result)
    {
        return result.IsSuccess ? result.Value : throw UsersApiException.FromProblem(result.Outcome, result.Problem);
    }

    private static void EnsureSuccess(ApiCallResult result)
    {
        if (!result.IsSuccess) throw UsersApiException.FromProblem(result.Outcome, result.Problem);
    }
}
