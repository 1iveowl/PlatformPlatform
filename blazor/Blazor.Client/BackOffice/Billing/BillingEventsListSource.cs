// The back office's billing events list's side of DataList. Its URL keeps the React back office's names and values: search as
// text, view as all, mrr, state or other with all left out, orderBy as a SortableBillingEventProperties name with OccurredAt
// left out, sortOrder only when it is Ascending, and pageOffset. Each view is a fixed set of event types, the React back
// office's billingEventCategories.ts; the MRR and state views overlap, as there. A malformed view is dropped.

using Account.Client;
using Account.Features.BackOffice.BillingEvents.Queries;
using Account.Features.BackOffice.Requests;
using Account.Features.Subscriptions.Domain;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Billing;

public enum BillingEventsView
{
    All,
    Mrr,
    State,
    Other
}

public static class BillingEventsListSource
{
    public const string ListId = "back-office-billing-events";
    public const string SearchParameter = "search";
    public const string ViewParameter = "view";
    public const string DefaultOrderBy = nameof(SortableBillingEventProperties.OccurredAt);
    public const SortOrder DefaultSortOrder = SortOrder.Descending;

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, ViewParameter];

    public static readonly IReadOnlyList<BillingEventsView> Views = [BillingEventsView.All, BillingEventsView.Mrr, BillingEventsView.State, BillingEventsView.Other];

    // Every change of committed recurring revenue, scheduled downgrades and their cancellation included
    public static readonly IReadOnlyList<BillingEventType> MrrImpactEventTypes =
    [
        BillingEventType.SubscriptionCreated, BillingEventType.SubscriptionUpgraded, BillingEventType.SubscriptionDowngradeScheduled,
        BillingEventType.SubscriptionDowngradeCancelled, BillingEventType.SubscriptionDowngraded, BillingEventType.SubscriptionReactivated,
        BillingEventType.SubscriptionCancelled, BillingEventType.SubscriptionExpired, BillingEventType.SubscriptionImmediatelyCancelled
    ];

    // Only the events after which the plan the customer is on has changed
    public static readonly IReadOnlyList<BillingEventType> SubscriptionStateEventTypes =
    [
        BillingEventType.SubscriptionCreated, BillingEventType.SubscriptionUpgraded, BillingEventType.SubscriptionDowngraded,
        BillingEventType.SubscriptionExpired, BillingEventType.SubscriptionImmediatelyCancelled, BillingEventType.SubscriptionSuspended
    ];

    // Payment flow, billing details and same-plan renewals, which change neither
    public static readonly IReadOnlyList<BillingEventType> OtherEventTypes =
    [
        BillingEventType.SubscriptionRenewed, BillingEventType.SubscriptionPastDue, BillingEventType.PaymentFailed, BillingEventType.PaymentRecovered,
        BillingEventType.PaymentRefunded, BillingEventType.BillingInfoAdded, BillingEventType.BillingInfoUpdated, BillingEventType.PaymentMethodUpdated,
        BillingEventType.NoOp, BillingEventType.Unclassified
    ];

    public static string ToValue(BillingEventsView view)
    {
        return view switch
        {
            BillingEventsView.Mrr => "mrr",
            BillingEventsView.State => "state",
            BillingEventsView.Other => "other",
            _ => "all"
        };
    }

    // Exact values only, as the React router's schema accepts them; anything else is All
    public static BillingEventsView GetView(IReadOnlyDictionary<string, string> filters)
    {
        var value = filters.GetValueOrDefault(ViewParameter)?.Trim();
        return Views.FirstOrDefault(view => string.Equals(ToValue(view), value, StringComparison.Ordinal));
    }

    public static IReadOnlyList<BillingEventType> GetEventTypes(BillingEventsView view)
    {
        return view switch
        {
            BillingEventsView.Mrr => MrrImpactEventTypes,
            BillingEventsView.State => SubscriptionStateEventTypes,
            BillingEventsView.Other => OtherEventTypes,
            _ => []
        };
    }

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (GetView(filters) is var view && view != BillingEventsView.All) normalized[ViewParameter] = ToValue(view);
        return normalized;
    }

    // The filter change a view toggle makes; All is the default and is left out of the URL
    public static IReadOnlyDictionary<string, string?> SetView(BillingEventsView view)
    {
        return new Dictionary<string, string?> { [ViewParameter] = view == BillingEventsView.All ? null : ToValue(view) };
    }

    public static IReadOnlyDictionary<string, string?> ClearAllFilters()
    {
        return FilterParameters.ToDictionary(parameter => parameter, string? (_) => null);
    }

    // A row opens the account's Billing events tab, as the React rows do
    public static string AccountUrl(TenantId tenantId)
    {
        return AccountDetailTabs.ToUrl(tenantId, AccountDetailTab.BillingEvents);
    }

    public static GetBackOfficeBillingEventsQuery ToQuery(DataListRequest request)
    {
        return new GetBackOfficeBillingEventsQuery(
            request.Filters.GetValueOrDefault(SearchParameter),
            [.. GetEventTypes(GetView(request.Filters))],
            OrderBy: InvoicesListSource.ParseOrderBy<SortableBillingEventProperties>(request.OrderBy) ?? SortableBillingEventProperties.OccurredAt,
            SortOrder: request.SortOrder,
            PageOffset: request.PageOffset,
            PageSize: request.PageSize
        );
    }

    public static async Task<DataListFetchResult<BillingEventSummary>> FetchAsync(BackOfficeClient backOfficeClient, GetBackOfficeBillingEventsQuery query, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetBillingEventsAsync(query, cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<BillingEventSummary>.Success(result.Value.BillingEvents, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<BillingEventSummary>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
