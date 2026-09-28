// The account detail's Billing events tab's side of DataList: GET /api/back-office/billing-events with TenantId, so the tab
// shows that account's events only, in the server's order, newest first. The page can show several lists, so this one's page
// is billingEventsPageOffset. The React tab shows the newest 50 events without paging and keeps no URL state; this tab pages
// through all of them. The list id carries the tenant id, which keeps one account's cached pages from answering for another's.

using System.Globalization;
using Account.Features.BackOffice.Requests;
using Blazor.Client.Components.Lists;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Accounts;

public static class AccountBillingEventsListSource
{
    public const string ParameterPrefix = "billingEvents";

    // No sort keys: the default is never written and an orderBy in the URL is ignored
    public const string DefaultOrderBy = "default";

    public static string ListId(TenantId tenantId)
    {
        return $"back-office-account-billing-events-{tenantId.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    public static GetBackOfficeBillingEventsQuery ToQuery(TenantId tenantId, DataListRequest request)
    {
        return new GetBackOfficeBillingEventsQuery(TenantId: tenantId, PageOffset: request.PageOffset, PageSize: request.PageSize);
    }
}
