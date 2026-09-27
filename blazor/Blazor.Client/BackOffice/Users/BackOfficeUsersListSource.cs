// The back office's users list's side of DataList. Its URL keeps the React back office's names and value forms, so a link
// means the same in both editions: search as text, roles as a JSON array of role names (the form the React router writes,
// ["Owner","Admin"]), activity as one UserActivityFilter name, and the wrapper's pageOffset. The React list has no sort, so
// neither has this one: the account API orders the users by last seen, newest first. Malformed filter values are dropped
// rather than sent; the API validates the rest.

using Account.Client;
using Account.Features.Users.BackOffice.Queries;
using Account.Features.Users.BackOffice.Requests;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Users;

public static class BackOfficeUsersListSource
{
    public const string ListId = "back-office-users";
    public const string SearchParameter = "search";
    public const string RolesParameter = "roles";
    public const string ActivityParameter = "activity";

    // No sort keys: the default is never written and an orderBy in the URL is ignored
    public const string DefaultOrderBy = "default";

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, RolesParameter, ActivityParameter];

    // The React toolbar's order, which is also the canonical order of the values in the URL
    public static readonly IReadOnlyList<UserRole> Roles = [UserRole.Owner, UserRole.Admin, UserRole.Member];

    public static readonly IReadOnlyList<UserActivityFilter> Activities =
        [UserActivityFilter.ActiveLast24Hours, UserActivityFilter.ActiveLast7Days, UserActivityFilter.ActiveLast30Days, UserActivityFilter.InactiveOver30Days];

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (AccountsListSource.FormatValues(GetRoles(filters)) is { } roles) normalized[RolesParameter] = roles;
        if (GetActivity(filters) is { } activity) normalized[ActivityParameter] = activity.ToString();
        return normalized;
    }

    public static IReadOnlyList<UserRole> GetRoles(IReadOnlyDictionary<string, string> filters)
    {
        return AccountsListSource.ParseValues(filters.GetValueOrDefault(RolesParameter), Roles);
    }

    // One exact enum name, as the React router's schema accepts it; anything else is no filter
    public static UserActivityFilter? GetActivity(IReadOnlyDictionary<string, string> filters)
    {
        var value = filters.GetValueOrDefault(ActivityParameter)?.Trim();
        return Activities.Cast<UserActivityFilter?>().FirstOrDefault(activity => string.Equals(activity.ToString(), value, StringComparison.Ordinal));
    }

    // The filter change a role toggle makes: the role added or removed, and the parameter left out when none remains
    public static IReadOnlyDictionary<string, string?> ToggleRole(IReadOnlyDictionary<string, string> filters, UserRole role)
    {
        return new Dictionary<string, string?> { [RolesParameter] = AccountsListSource.FormatValues(AccountsListSource.Toggle(GetRoles(filters), role, Roles)) };
    }

    // Activity is one value: choosing the selected one clears it, choosing another replaces it
    public static IReadOnlyDictionary<string, string?> ToggleActivity(IReadOnlyDictionary<string, string> filters, UserActivityFilter activity)
    {
        return new Dictionary<string, string?> { [ActivityParameter] = GetActivity(filters) == activity ? null : activity.ToString() };
    }

    public static IReadOnlyDictionary<string, string?> ClearAllFilters()
    {
        return FilterParameters.ToDictionary(parameter => parameter, string? (_) => null);
    }

    // The user's detail page below the back office, keyed by the user id as in the React back office
    public static string UserUrl(UserId userId)
    {
        return BackOfficeUrls.ToAbsolute($"users/{Uri.EscapeDataString(userId.Value)}");
    }

    // The route value of the user's page; a value that is not a user id is no user, which the page shows as not found
    public static UserId? ParseUserId(string? value)
    {
        return UserId.TryParse(value, out var userId) ? userId : null;
    }

    public static GetBackOfficeUsersQuery ToQuery(DataListRequest request)
    {
        return new GetBackOfficeUsersQuery(
            request.Filters.GetValueOrDefault(SearchParameter),
            [.. GetRoles(request.Filters)],
            GetActivity(request.Filters),
            PageOffset: request.PageOffset,
            PageSize: request.PageSize
        );
    }

    public static async Task<DataListFetchResult<BackOfficeUserSummary>> FetchAsync(BackOfficeClient backOfficeClient, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetUsersAsync(ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<BackOfficeUserSummary>.Success(result.Value.Users, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<BackOfficeUserSummary>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
