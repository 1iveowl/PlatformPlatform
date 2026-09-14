// Spike code (Blazor edition, stage B3): the users calls of the account API through the gateway, and the page cache that
// both grids read. The account API pages by PageOffset and PageSize (at most 1000); a virtualized grid asks for arbitrary
// index ranges, so ranges are served from fixed server pages that are fetched once per filter state.

using System.Net;
using Blazor.Client.Bootstrap;

namespace Blazor.Client.Users;

public sealed class UsersApiException(HttpStatusCode statusCode, string body)
    : Exception($"The account API returned {(int)statusCode}: {body}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class UsersApiClient(HttpClient httpClient, IBootstrapSource bootstrapSource)
{
    public const int VirtualFetchPageSize = 100;
    public const int PagedPageSize = 25;

    private readonly Dictionary<(string Query, int PageSize, int PageOffset), Task<UsersResponse>> _pages = new();
    private string? _antiforgeryToken;

    public int RequestCount { get; private set; }

    public void Invalidate()
    {
        _pages.Clear();
    }

    public Task<UsersResponse> GetPageAsync(UsersListState state, int pageOffset, int pageSize)
    {
        var key = (state.ToApiQuery(), pageSize, pageOffset);
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
        using var response = await httpClient.GetAsync($"/api/account/users/{Uri.EscapeDataString(id)}");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UserDetails>())!;
    }

    public Task ChangeRoleAsync(string id, UserRole role)
    {
        return SendAsync(HttpMethod.Put, $"/api/account/users/{Uri.EscapeDataString(id)}/change-user-role", new { userRole = role.ToString() });
    }

    public Task DeleteAsync(string id)
    {
        return SendAsync(HttpMethod.Delete, $"/api/account/users/{Uri.EscapeDataString(id)}", null);
    }

    public Task BulkDeleteAsync(IEnumerable<string> ids)
    {
        return SendAsync(HttpMethod.Post, "/api/account/users/bulk-delete", new { userIds = ids.ToArray() });
    }

    private async Task<UsersResponse> FetchPageAsync(string query, int pageOffset, int pageSize)
    {
        RequestCount++;
        // The API rejects a PageOffset at or beyond the last page, which includes offset 0 of an empty result, so page 0 omits it
        var paging = pageOffset == 0 ? $"PageSize={pageSize}" : $"PageSize={pageSize}&PageOffset={pageOffset}";
        using var response = await httpClient.GetAsync($"/api/account/users?{query}&{paging}");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UsersResponse>())!;
    }

    private async Task SendAsync(HttpMethod method, string url, object? body)
    {
        _antiforgeryToken ??= (await bootstrapSource.GetAsync()).AntiforgeryToken;
        using var request = new HttpRequestMessage(method, url);
        request.Content = body is null ? null : JsonContent.Create(body);
        request.Headers.Add("x-xsrf-token", _antiforgeryToken);
        using var response = await httpClient.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new UsersApiException(response.StatusCode, body[..Math.Min(body.Length, 300)]);
    }
}
