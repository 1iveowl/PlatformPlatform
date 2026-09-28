using Account.Features.BackOffice.BillingEvents.Queries;
using Account.Features.BackOffice.Invoices.Queries;
using Account.Features.Subscriptions.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;
using SharedKernel.Persistence;

namespace Account.Features.BackOffice.Requests;

// Requests of the back-office endpoints. Each record keeps the name and JSON shape of the command it is mapped to. The tenant
// id of SetTenantAbInclusionPinCommand travels in the route, not in the body; a null pin clears it.
[PublicAPI]
public sealed record SetTenantAbInclusionPinCommand(AbInclusionPin? AbInclusionPin);

// The query string of GET /api/back-office/invoices, which binds GetBackOfficeInvoicesQuery with [AsParameters]: PascalCase
// property names, one repeated Statuses parameter per status, enum names, and the server's defaults of Date, descending, the
// first page and 25 rows.
[PublicAPI]
public sealed record GetBackOfficeInvoicesQuery(
    string? Search = null,
    BackOfficeInvoiceStatusFilter[]? Statuses = null,
    SortableBackOfficeInvoiceProperties OrderBy = SortableBackOfficeInvoiceProperties.Date,
    SortOrder SortOrder = SortOrder.Descending,
    int PageOffset = 0,
    int PageSize = 25
);

// The query string of GET /api/back-office/billing-events, which binds GetBackOfficeBillingEventsQuery with [AsParameters]:
// one repeated EventTypes parameter per type, TenantId for one account's events, and the server's defaults of OccurredAt,
// descending, the first page and 25 rows. The server's OccurredFrom and OccurredTo have no caller in the back office.
[PublicAPI]
public sealed record GetBackOfficeBillingEventsQuery(
    string? Search = null,
    BillingEventType[]? EventTypes = null,
    TenantId? TenantId = null,
    SortableBillingEventProperties OrderBy = SortableBillingEventProperties.OccurredAt,
    SortOrder SortOrder = SortOrder.Descending,
    int PageOffset = 0,
    int PageSize = 25
);
