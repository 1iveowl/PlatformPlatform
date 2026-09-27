// The account detail's users tab's side of DataList. The page can show three lists, so this one's parameters carry the users
// prefix: usersSearch as text, usersRoles as a JSON array of role names in the accounts list's form (["Owner","Admin"]), and
// the wrapper's usersPageOffset. The React back office keeps the same search and roles in component state only, so there is
// no React URL form to match. The account API orders the users itself, so the list has no sort keys. The list id carries the
// tenant id, which keeps one account's cached pages from answering for another's.

using System.Globalization;
using Account.Client;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Tenants.BackOffice.Requests;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Accounts;

public static class AccountUsersListSource
{
    public const string ParameterPrefix = "users";
    public const string SearchParameter = "usersSearch";
    public const string RolesParameter = "usersRoles";

    // No sort keys: the default is never written and an orderBy in the URL is ignored
    public const string DefaultOrderBy = "default";

    // The React toolbar's order, which is also the canonical order of the values in the URL
    public static readonly IReadOnlyList<UserRole> Roles = [UserRole.Owner, UserRole.Admin, UserRole.Member];

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, RolesParameter];

    public static string ListId(TenantId tenantId)
    {
        return $"back-office-account-users-{tenantId.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (AccountsListSource.FormatValues(GetRoles(filters)) is { } roles) normalized[RolesParameter] = roles;
        return normalized;
    }

    public static IReadOnlyList<UserRole> GetRoles(IReadOnlyDictionary<string, string> filters)
    {
        return AccountsListSource.ParseValues(filters.GetValueOrDefault(RolesParameter), Roles);
    }

    // The filter change a role toggle makes: the role added or removed, and the parameter left out when none remains
    public static IReadOnlyDictionary<string, string?> ToggleRole(IReadOnlyDictionary<string, string> filters, UserRole role)
    {
        return new Dictionary<string, string?> { [RolesParameter] = AccountsListSource.FormatValues(AccountsListSource.Toggle(GetRoles(filters), role, Roles)) };
    }

    public static GetTenantUsersQuery ToQuery(DataListRequest request)
    {
        return new GetTenantUsersQuery(request.Filters.GetValueOrDefault(SearchParameter), [.. GetRoles(request.Filters)], request.PageOffset, request.PageSize);
    }

    public static async Task<DataListFetchResult<TenantUserSummary>> FetchAsync(BackOfficeClient backOfficeClient, TenantId tenantId, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetTenantUsersAsync(tenantId, ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<TenantUserSummary>.Success(result.Value.Users, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<TenantUserSummary>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
