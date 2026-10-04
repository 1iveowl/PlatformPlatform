// The flag detail's tenants list's side of DataList, the React back office's TenantOverridesSection and
// PlanFeatureFlagTenantsSection. Its parameters carry the tenants prefix and keep React's names, so a link means the same in
// both editions: tenantsSearch as text, tenantsPlans as a JSON array of plan names (["Premium","Basis"]), tenantsState as All
// or Disabled (Enabled is the default and left out), tenantsHasOverride as true, and the wrapper's tenantsOrderBy,
// tenantsSortOrder and tenantsPageOffset. An A/B test sorts by the inclusion threshold by default, any other flag by name. A
// plan-managed flag's list is read-only, has no state or override filter and no sort, and with no plan chosen shows the
// required plan and every plan above it, as React does. The list id carries the flag key, so one flag's cached pages never
// answer for another's.

using Account.Client;
using Account.Features.FeatureFlags.Queries;
using Account.Features.FeatureFlags.Requests;
using Account.Features.Subscriptions.Domain;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;

namespace Blazor.Client.BackOffice.FeatureFlags;

public static class FeatureFlagTenantsListSource
{
    public const string ParameterPrefix = "tenants";
    public const string SearchParameter = "tenantsSearch";
    public const string PlansParameter = "tenantsPlans";
    public const string StateParameter = "tenantsState";
    public const string HasOverrideParameter = "tenantsHasOverride";

    // The plan-managed list has no sort keys: its default is never written and an orderBy in the URL is ignored
    public const string PlanManagedOrderBy = "default";

    private const string TrueValue = "true";

    // The React toolbar's order, which is also the canonical order of the values in the URL
    public static readonly IReadOnlyList<SubscriptionPlan> Plans = [SubscriptionPlan.Premium, SubscriptionPlan.Standard, SubscriptionPlan.Basis];

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, PlansParameter, StateParameter, HasOverrideParameter];

    // Lowest to highest: a plan-managed flag is on for its required plan and every plan above it
    private static readonly IReadOnlyList<SubscriptionPlan> PlanOrder = [SubscriptionPlan.Basis, SubscriptionPlan.Standard, SubscriptionPlan.Premium];

    public static string ListId(string flagKey)
    {
        return $"back-office-feature-flag-tenants-{flagKey}";
    }

    public static string DefaultOrderBy(FeatureFlagInfo flag)
    {
        if (FeatureFlagDetail.IsPlanFlag(flag)) return PlanManagedOrderBy;
        return flag.IsAbTestEligible ? nameof(SortableFeatureFlagTenantProperties.InclusionThresholdPercentage) : nameof(SortableFeatureFlagTenantProperties.Name);
    }

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (DataListQueryValues.FormatValues(GetPlans(filters)) is { } plans) normalized[PlansParameter] = plans;
        if (FeatureFlagOverrides.FormatState(GetState(filters)) is { } state) normalized[StateParameter] = state;
        if (HasOverride(filters)) normalized[HasOverrideParameter] = TrueValue;
        return normalized;
    }

    public static IReadOnlyList<SubscriptionPlan> GetPlans(IReadOnlyDictionary<string, string> filters)
    {
        return DataListQueryValues.ParseValues(filters.GetValueOrDefault(PlansParameter), Plans);
    }

    public static FeatureFlagStateFilter GetState(IReadOnlyDictionary<string, string> filters)
    {
        return FeatureFlagOverrides.ParseState(filters.GetValueOrDefault(StateParameter));
    }

    public static bool HasOverride(IReadOnlyDictionary<string, string> filters)
    {
        return string.Equals(filters.GetValueOrDefault(HasOverrideParameter)?.Trim(), TrueValue, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, string?> TogglePlan(IReadOnlyDictionary<string, string> filters, SubscriptionPlan plan)
    {
        return new Dictionary<string, string?> { [PlansParameter] = DataListQueryValues.FormatValues(DataListQueryValues.Toggle(GetPlans(filters), plan, Plans)) };
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

    // The plans a plan-managed flag's list shows when none is chosen: the required plan and every plan above it
    public static IReadOnlyList<SubscriptionPlan> GetDefaultPlans(string? requiredPlan)
    {
        var index = PlanOrder.ToList().FindIndex(plan => string.Equals(plan.ToString(), requiredPlan, StringComparison.Ordinal));
        return index < 0 ? [] : PlanOrder.Skip(index).ToArray();
    }

    public static GetFeatureFlagTenantsQuery ToQuery(FeatureFlagInfo flag, DataListRequest request)
    {
        var plans = GetPlans(request.Filters);
        var search = request.Filters.GetValueOrDefault(SearchParameter);
        if (FeatureFlagDetail.IsPlanFlag(flag))
        {
            var effectivePlans = plans.Count == 0 ? GetDefaultPlans(flag.RequiredPlan) : plans;
            return new GetFeatureFlagTenantsQuery(search, [.. effectivePlans], PageOffset: request.PageOffset, PageSize: request.PageSize);
        }

        return new GetFeatureFlagTenantsQuery(
            search,
            [.. plans],
            FeatureFlagOverrides.ToApiState(GetState(request.Filters)),
            HasOverride(request.Filters),
            DataListQueryValues.ParseName<SortableFeatureFlagTenantProperties>(request.OrderBy, StringComparison.OrdinalIgnoreCase) ?? SortableFeatureFlagTenantProperties.Name,
            request.SortOrder,
            request.PageOffset,
            request.PageSize
        );
    }

    public static async Task<DataListFetchResult<FeatureFlagTenantInfo>> FetchAsync(BackOfficeClient backOfficeClient, FeatureFlagInfo flag, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetFeatureFlagTenantsAsync(flag.Key, ToQuery(flag, request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<FeatureFlagTenantInfo>.Success(result.Value.Tenants, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<FeatureFlagTenantInfo>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
