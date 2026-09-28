// The account detail's Invoices tab's side of DataList: the account's invoice, refund and credit note rows from
// GET /api/back-office/tenants/{id}/payment-history in the server's order, newest first. The page can show several lists, so
// this one's page is invoicesPageOffset. The React tab keeps its page in component state only, so there is no React URL form
// to match. The list id carries the tenant id, which keeps one account's cached pages from answering for another's.

using System.Globalization;
using Account.Client;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Tenants.BackOffice.Requests;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Accounts;

public static class AccountInvoicesListSource
{
    public const string ParameterPrefix = "invoices";

    // No sort keys: the default is never written and an orderBy in the URL is ignored
    public const string DefaultOrderBy = "default";

    public static string ListId(TenantId tenantId)
    {
        return $"back-office-account-invoices-{tenantId.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    // One transaction can give an invoice row and a reversal row, so the row kind is part of the key
    public static string KeyOf(TenantPaymentTransaction transaction)
    {
        return $"{transaction.Id.Value}-{transaction.RowKind}";
    }

    public static GetTenantPaymentHistoryQuery ToQuery(DataListRequest request)
    {
        return new GetTenantPaymentHistoryQuery(request.PageOffset, request.PageSize);
    }

    public static async Task<DataListFetchResult<TenantPaymentTransaction>> FetchAsync(BackOfficeClient backOfficeClient, TenantId tenantId, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetTenantPaymentHistoryAsync(tenantId, ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<TenantPaymentTransaction>.Success(result.Value.Transactions, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<TenantPaymentTransaction>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
