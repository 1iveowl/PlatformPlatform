// The user detail's Accounts tab's side of DataList: the accounts the person is a member of, from the user's detail response,
// paged in memory in the order the account API returns them. The tab's page parameter carries the accounts prefix, and the
// list id carries the user id, which keeps one user's cached pages from answering for another's.

using Account.Features.Users.BackOffice.Queries;
using Blazor.Client.Components.Lists;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Users;

public static class UserMembershipsListSource
{
    public const string ParameterPrefix = "accounts";

    // No sort keys: the memberships keep the account API's order
    public const string DefaultOrderBy = "default";

    public static string ListId(UserId userId)
    {
        return $"back-office-user-accounts-{userId.Value}";
    }

    public static DataListFetchResult<BackOfficeUserTenantMembership> Page(IReadOnlyList<BackOfficeUserTenantMembership> memberships, DataListRequest request)
    {
        return DataListFetchResult<BackOfficeUserTenantMembership>.Success(memberships.Skip(request.PageOffset * request.PageSize).Take(request.PageSize).ToArray(), memberships.Count);
    }
}
