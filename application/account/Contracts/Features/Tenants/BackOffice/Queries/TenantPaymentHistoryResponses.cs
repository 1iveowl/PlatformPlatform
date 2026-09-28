using Account.Features.BackOffice.Invoices.Queries;
using Account.Features.Subscriptions.Domain;
using JetBrains.Annotations;

namespace Account.Features.Tenants.BackOffice.Queries;

// One account's invoices, refunds and credit notes as GET /api/back-office/tenants/{id}/payment-history returns them. The
// query record and its handler stay in the account API.

[PublicAPI]
public sealed record TenantPaymentHistoryResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, TenantPaymentTransaction[] Transactions);

[PublicAPI]
public sealed record TenantPaymentTransaction(
    PaymentTransactionId Id,
    BackOfficeInvoiceRowKind RowKind,
    decimal Amount,
    decimal AmountExcludingTax,
    decimal TaxAmount,
    string Currency,
    PaymentTransactionStatus Status,
    DateTimeOffset Date,
    DateTimeOffset? RefundedAt,
    string? FailureReason,
    string? InvoiceUrl,
    string? CreditNoteUrl,
    DateTimeOffset? CreditNotedAt,
    SubscriptionPlan? Plan
);
