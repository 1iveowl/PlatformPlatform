// The back office's accounts list's side of DataList. Its URL keeps the React back office's names and value forms, so a link
// means the same in both editions: search as text, plans and statuses as a JSON array of enum names (the form the React
// router writes, ["Premium","Standard"]), unsynced and driftDetected as true, orderBy as a SortableTenantProperties name,
// sortOrder only when it is Ascending, and no orderBy for the server's default order by last modification. Malformed filter
// values are dropped rather than sent; the API validates the rest.

using Account.Client;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Tenants.BackOffice.Requests;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;

namespace Blazor.Client.BackOffice.Accounts;

public static class AccountsListSource
{
    public const string ListId = "back-office-accounts";
    public const string SelectedKeyParameter = "tenantId";
    public const string SearchParameter = "search";
    public const string PlansParameter = "plans";
    public const string StatusesParameter = "statuses";
    public const string UnsyncedParameter = "unsynced";
    public const string DriftDetectedParameter = "driftDetected";
    public const string DefaultOrderBy = nameof(SortableTenantProperties.ModifiedAt);
    public const SortOrder DefaultSortOrder = SortOrder.Descending;

    // A back-office document serves one back-office identity for its whole life: another identity signs in through the
    // platform login, which loads a new document and a new runtime, so the cached pages need no identity key of their own
    public const string CacheScope = "back-office";

    private const string TrueValue = "true";

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, PlansParameter, StatusesParameter, UnsyncedParameter, DriftDetectedParameter];

    // The React toolbar's order, which is also the canonical order of the values in the URL
    public static readonly IReadOnlyList<SubscriptionPlan> Plans = [SubscriptionPlan.Premium, SubscriptionPlan.Standard, SubscriptionPlan.Basis];

    public static readonly IReadOnlyList<TenantStatusFilter> Statuses =
        [TenantStatusFilter.Active, TenantStatusFilter.Downgrading, TenantStatusFilter.Canceling, TenantStatusFilter.Canceled, TenantStatusFilter.Free];

    // ModifiedAt, the server's default, is a sort key without a column
    public static readonly IReadOnlyList<string> HiddenSortKeys = [nameof(SortableTenantProperties.ModifiedAt)];

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (DataListQueryValues.FormatValues(DataListQueryValues.ParseValues(filters.GetValueOrDefault(PlansParameter), Plans)) is { } plans) normalized[PlansParameter] = plans;
        if (DataListQueryValues.FormatValues(DataListQueryValues.ParseValues(filters.GetValueOrDefault(StatusesParameter), Statuses)) is { } statuses) normalized[StatusesParameter] = statuses;
        if (IsTrue(filters.GetValueOrDefault(UnsyncedParameter))) normalized[UnsyncedParameter] = TrueValue;
        if (IsTrue(filters.GetValueOrDefault(DriftDetectedParameter))) normalized[DriftDetectedParameter] = TrueValue;
        return normalized;
    }

    public static IReadOnlyList<SubscriptionPlan> GetPlans(IReadOnlyDictionary<string, string> filters)
    {
        return DataListQueryValues.ParseValues(filters.GetValueOrDefault(PlansParameter), Plans);
    }

    public static IReadOnlyList<TenantStatusFilter> GetStatuses(IReadOnlyDictionary<string, string> filters)
    {
        return DataListQueryValues.ParseValues(filters.GetValueOrDefault(StatusesParameter), Statuses);
    }

    public static bool IsUnsynced(IReadOnlyDictionary<string, string> filters)
    {
        return IsTrue(filters.GetValueOrDefault(UnsyncedParameter));
    }

    public static bool IsDriftDetected(IReadOnlyDictionary<string, string> filters)
    {
        return IsTrue(filters.GetValueOrDefault(DriftDetectedParameter));
    }

    // The filter change a toolbar toggle makes: the plan added or removed, and the parameter left out when none remains
    public static IReadOnlyDictionary<string, string?> TogglePlan(IReadOnlyDictionary<string, string> filters, SubscriptionPlan plan)
    {
        return new Dictionary<string, string?> { [PlansParameter] = DataListQueryValues.FormatValues(DataListQueryValues.Toggle(GetPlans(filters), plan, Plans)) };
    }

    public static IReadOnlyDictionary<string, string?> ToggleStatus(IReadOnlyDictionary<string, string> filters, TenantStatusFilter status)
    {
        return new Dictionary<string, string?> { [StatusesParameter] = DataListQueryValues.FormatValues(DataListQueryValues.Toggle(GetStatuses(filters), status, Statuses)) };
    }

    public static IReadOnlyDictionary<string, string?> ClearFilter(string parameter)
    {
        return new Dictionary<string, string?> { [parameter] = null };
    }

    public static IReadOnlyDictionary<string, string?> ClearAllFilters()
    {
        return FilterParameters.ToDictionary(parameter => parameter, string? (_) => null);
    }

    // The link a banner or a dashboard tile opens: the list below the back office with the given filters in their URL form
    public static string ToUrl(IReadOnlyDictionary<string, string> filters)
    {
        var query = string.Join('&', FilterParameters.Where(filters.ContainsKey).Select(parameter => $"{parameter}={Uri.EscapeDataString(filters[parameter])}"));
        return BackOfficeUrls.ToAbsolute(query.Length == 0 ? "accounts" : $"accounts?{query}");
    }

    // The list filtered to the given statuses, as the dashboard's MRR tile links to it
    public static string ToUrl(IReadOnlyList<TenantStatusFilter> statuses)
    {
        return DataListQueryValues.FormatValues(statuses) is { } value ? ToUrl(new Dictionary<string, string> { [StatusesParameter] = value }) : ToUrl(new Dictionary<string, string>());
    }

    public static GetTenantsQuery ToQuery(DataListRequest request)
    {
        return new GetTenantsQuery(
            request.Filters.GetValueOrDefault(SearchParameter),
            [.. GetPlans(request.Filters)],
            [.. GetStatuses(request.Filters)],
            IsUnsynced(request.Filters),
            IsDriftDetected(request.Filters),
            DataListQueryValues.ParseName<SortableTenantProperties>(request.OrderBy, StringComparison.OrdinalIgnoreCase) ?? SortableTenantProperties.ModifiedAt,
            request.SortOrder,
            request.PageOffset,
            request.PageSize
        );
    }

    public static async Task<DataListFetchResult<TenantSummary>> FetchAsync(BackOfficeClient backOfficeClient, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetTenantsAsync(ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<TenantSummary>.Success(result.Value.Tenants, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<TenantSummary>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value?.Trim(), TrueValue, StringComparison.OrdinalIgnoreCase);
    }
}
