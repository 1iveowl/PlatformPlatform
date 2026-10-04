// The flag detail's users list's side of DataList, the React back office's UserOverridesSection. Its parameters carry the
// users prefix and keep React's names: usersSearch as text, usersRoles as a JSON array of role names (["Owner","Admin"]),
// usersState as All or Disabled (Enabled is the default and left out), usersHasOverride as true, and the wrapper's
// usersOrderBy, usersSortOrder and usersPageOffset. They never touch the tenants list's parameters, so the two lists filter and
// page independently. An A/B test sorts by the inclusion threshold by default, any other flag by name. The list id carries
// the flag key.

using Account.Client;
using Account.Features.FeatureFlags.Queries;
using Account.Features.FeatureFlags.Requests;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;

namespace Blazor.Client.BackOffice.FeatureFlags;

public static class FeatureFlagUsersListSource
{
    public const string ParameterPrefix = "users";
    public const string SearchParameter = "usersSearch";
    public const string RolesParameter = "usersRoles";
    public const string StateParameter = "usersState";
    public const string HasOverrideParameter = "usersHasOverride";

    private const string TrueValue = "true";

    // The React toolbar's order, which is also the canonical order of the values in the URL
    public static readonly IReadOnlyList<UserRole> Roles = [UserRole.Owner, UserRole.Admin, UserRole.Member];

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, RolesParameter, StateParameter, HasOverrideParameter];

    public static string ListId(string flagKey)
    {
        return $"back-office-feature-flag-users-{flagKey}";
    }

    public static string DefaultOrderBy(FeatureFlagInfo flag)
    {
        return flag.IsAbTestEligible ? nameof(SortableFeatureFlagUserProperties.InclusionThresholdPercentage) : nameof(SortableFeatureFlagUserProperties.Name);
    }

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (DataListQueryValues.FormatValues(GetRoles(filters)) is { } roles) normalized[RolesParameter] = roles;
        if (FeatureFlagOverrides.FormatState(GetState(filters)) is { } state) normalized[StateParameter] = state;
        if (HasOverride(filters)) normalized[HasOverrideParameter] = TrueValue;
        return normalized;
    }

    public static IReadOnlyList<UserRole> GetRoles(IReadOnlyDictionary<string, string> filters)
    {
        return DataListQueryValues.ParseValues(filters.GetValueOrDefault(RolesParameter), Roles);
    }

    public static FeatureFlagStateFilter GetState(IReadOnlyDictionary<string, string> filters)
    {
        return FeatureFlagOverrides.ParseState(filters.GetValueOrDefault(StateParameter));
    }

    public static bool HasOverride(IReadOnlyDictionary<string, string> filters)
    {
        return string.Equals(filters.GetValueOrDefault(HasOverrideParameter)?.Trim(), TrueValue, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, string?> ToggleRole(IReadOnlyDictionary<string, string> filters, UserRole role)
    {
        return new Dictionary<string, string?> { [RolesParameter] = DataListQueryValues.FormatValues(DataListQueryValues.Toggle(GetRoles(filters), role, Roles)) };
    }

    public static IReadOnlyDictionary<string, string?> SetState(FeatureFlagStateFilter state)
    {
        return new Dictionary<string, string?> { [StateParameter] = FeatureFlagOverrides.FormatState(state) };
    }

    public static IReadOnlyDictionary<string, string?> ToggleHasOverride(IReadOnlyDictionary<string, string> filters)
    {
        return new Dictionary<string, string?> { [HasOverrideParameter] = HasOverride(filters) ? null : TrueValue };
    }

    public static IReadOnlyDictionary<string, string?> ClearFilters()
    {
        return FilterParameters.ToDictionary(name => name, _ => (string?)null);
    }

    public static GetFeatureFlagUsersQuery ToQuery(DataListRequest request)
    {
        return new GetFeatureFlagUsersQuery(
            request.Filters.GetValueOrDefault(SearchParameter),
            [.. GetRoles(request.Filters)],
            FeatureFlagOverrides.ToApiState(GetState(request.Filters)),
            HasOverride(request.Filters),
            DataListQueryValues.ParseName<SortableFeatureFlagUserProperties>(request.OrderBy, StringComparison.OrdinalIgnoreCase) ?? SortableFeatureFlagUserProperties.Name,
            request.SortOrder,
            request.PageOffset,
            request.PageSize
        );
    }

    public static async Task<DataListFetchResult<FeatureFlagUserInfo>> FetchAsync(BackOfficeClient backOfficeClient, string flagKey, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetFeatureFlagUsersAsync(flagKey, ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<FeatureFlagUserInfo>.Success(result.Value.Users, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<FeatureFlagUserInfo>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
