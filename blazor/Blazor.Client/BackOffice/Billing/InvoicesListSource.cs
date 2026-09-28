// The back office's invoices list's side of DataList. Its URL keeps the React back office's names and values, so a link means
// the same in both editions: search as text, view as all, invoices or refunds with all left out, orderBy as a
// SortableBackOfficeInvoiceProperties name with Date left out, sortOrder only when it is Ascending, and pageOffset. The view is
// a fixed set of statuses, as in the React page: invoices are the paid, pending and failed invoice rows, refunds the refund and
// credit note rows, and all sends no status. A malformed view is dropped; the API validates the rest.

using Account.Client;
using Account.Features.BackOffice.Invoices.Queries;
using Account.Features.BackOffice.Requests;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Billing;

public enum InvoicesView
{
    All,
    Invoices,
    Refunds
}

public static class InvoicesListSource
{
    public const string ListId = "back-office-invoices";
    public const string SearchParameter = "search";
    public const string ViewParameter = "view";
    public const string DefaultOrderBy = nameof(SortableBackOfficeInvoiceProperties.Date);
    public const SortOrder DefaultSortOrder = SortOrder.Descending;

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, ViewParameter];

    public static readonly IReadOnlyList<InvoicesView> Views = [InvoicesView.All, InvoicesView.Invoices, InvoicesView.Refunds];

    public static string ToValue(InvoicesView view)
    {
        return view switch
        {
            InvoicesView.Invoices => "invoices",
            InvoicesView.Refunds => "refunds",
            _ => "all"
        };
    }

    // Exact values only, as the React router's schema accepts them; anything else is All
    public static InvoicesView GetView(IReadOnlyDictionary<string, string> filters)
    {
        var value = filters.GetValueOrDefault(ViewParameter)?.Trim();
        return Views.FirstOrDefault(view => string.Equals(ToValue(view), value, StringComparison.Ordinal));
    }

    public static IReadOnlyList<BackOfficeInvoiceStatusFilter> GetStatuses(InvoicesView view)
    {
        return view switch
        {
            InvoicesView.Invoices => [BackOfficeInvoiceStatusFilter.Paid, BackOfficeInvoiceStatusFilter.Pending, BackOfficeInvoiceStatusFilter.Failed],
            InvoicesView.Refunds => [BackOfficeInvoiceStatusFilter.Refunded, BackOfficeInvoiceStatusFilter.HasCreditNote],
            _ => []
        };
    }

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (GetView(filters) is var view && view != InvoicesView.All) normalized[ViewParameter] = ToValue(view);
        return normalized;
    }

    // The filter change a view toggle makes; All is the default and is left out of the URL
    public static IReadOnlyDictionary<string, string?> SetView(InvoicesView view)
    {
        return new Dictionary<string, string?> { [ViewParameter] = view == InvoicesView.All ? null : ToValue(view) };
    }

    public static IReadOnlyDictionary<string, string?> ClearAllFilters()
    {
        return FilterParameters.ToDictionary(parameter => parameter, string? (_) => null);
    }

    // One transaction can give an invoice row and a reversal row, so the row kind is part of the key
    public static string KeyOf(BackOfficeInvoiceSummary invoice)
    {
        return $"{invoice.Id.Value}-{invoice.RowKind}";
    }

    // A row opens the account's Invoices tab, as the React rows do
    public static string AccountUrl(TenantId tenantId)
    {
        return AccountDetailTabs.ToUrl(tenantId, AccountDetailTab.Invoices);
    }

    public static GetBackOfficeInvoicesQuery ToQuery(DataListRequest request)
    {
        return new GetBackOfficeInvoicesQuery(
            request.Filters.GetValueOrDefault(SearchParameter),
            [.. GetStatuses(GetView(request.Filters))],
            ParseOrderBy<SortableBackOfficeInvoiceProperties>(request.OrderBy) ?? SortableBackOfficeInvoiceProperties.Date,
            request.SortOrder,
            request.PageOffset,
            request.PageSize
        );
    }

    public static async Task<DataListFetchResult<BackOfficeInvoiceSummary>> FetchAsync(BackOfficeClient backOfficeClient, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetInvoicesAsync(ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<BackOfficeInvoiceSummary>.Success(result.Value.Invoices, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<BackOfficeInvoiceSummary>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }

    // Names only, as the React router's schema accepts them; Enum.TryParse would also accept numbers
    public static TEnum? ParseOrderBy<TEnum>(string? value) where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(candidate => string.Equals(candidate.ToString(), value, StringComparison.Ordinal));
    }
}
