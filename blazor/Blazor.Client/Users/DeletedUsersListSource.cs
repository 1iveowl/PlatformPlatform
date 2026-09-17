// The recycle bin's side of DataList: its list id, the fetch through the typed users client and the query it sends. The
// account API pages deleted users by offset and orders them by deletion time, newest first, so the list has no filters
// and no sortable columns; its default order is only the name DataList requires.

using Account.Client;
using Account.Features.Authentication.Queries;
using Account.Features.Users.Requests;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;

namespace Blazor.Client.Users;

public static class DeletedUsersListSource
{
    public const string ListId = "account-deleted-users";
    public const string DefaultOrderBy = "DeletedAt";

    // The identity the cached pages belong to, the same scope as the users list
    public static string CacheScope(BootstrapResponse bootstrap)
    {
        return UsersListSource.CacheScope(bootstrap);
    }

    // The API rejects an explicit offset on an empty result, so the first page omits it
    public static GetDeletedUsersQuery ToQuery(DataListRequest request)
    {
        return new GetDeletedUsersQuery(request.PageOffset == 0 ? null : request.PageOffset, request.PageSize);
    }

    public static async Task<DataListFetchResult<DeletedUserDetails>> FetchAsync(UsersClient usersClient, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await usersClient.GetDeletedUsersAsync(ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<DeletedUserDetails>.Success(result.Value.Users, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<DeletedUserDetails>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
