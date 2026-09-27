using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using JetBrains.Annotations;
using SharedKernel.Persistence;

namespace Account.Features.Tenants.BackOffice.Requests;

// Requests of the back office's tenant endpoints. GetTenantsQuery keeps the name and the query string parameters of the query
// GET /api/back-office/tenants binds with [AsParameters]: PascalCase property names, enum names, and one repeated parameter
// per value of Plans and Statuses. The server's defaults are ModifiedAt, descending, the first page and 25 rows.
[PublicAPI]
public sealed record GetTenantsQuery(
    string? Search = null,
    SubscriptionPlan[]? Plans = null,
    TenantStatusFilter[]? Statuses = null,
    bool Unsynced = false,
    bool DriftDetected = false,
    SortableTenantProperties OrderBy = SortableTenantProperties.ModifiedAt,
    SortOrder SortOrder = SortOrder.Descending,
    int PageOffset = 0,
    int PageSize = 25
);
