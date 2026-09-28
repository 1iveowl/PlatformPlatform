using Account.Features.Subscriptions.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.BackOffice.Invoices.Queries;

// The back office's invoices list as GET /api/back-office/invoices returns it, and the row kind, status filter and sort values
// the query binds. The query record and the handler that projects the rows stay in the account API.

[PublicAPI]
public sealed record BackOfficeInvoicesResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, BackOfficeInvoiceSummary[] Invoices);

[PublicAPI]
public sealed record BackOfficeInvoiceSummary(
    PaymentTransactionId Id,
    BackOfficeInvoiceRowKind RowKind,
    TenantId TenantId,
    string TenantName,
    string? TenantLogoUrl,
    DateTimeOffset Date,
    SubscriptionPlan? Plan,
    decimal Amount,
    decimal AmountExcludingTax,
    decimal TaxAmount,
    string Currency,
    PaymentTransactionStatus Status,
    string? FailureReason,
    string? InvoiceUrl,
    string? CreditNoteUrl,
    DateTimeOffset? CreditNotedAt,
    DateTimeOffset? RefundedAt
);

/// <summary>
///     Each PaymentTransaction projects to one Invoice row (always) plus an optional reversal row:
///     either CreditNote (when Stripe issued a credit note) or Refund (the edge case where a Stripe
///     pro-rated refund happened without a credit note). The Invoice row always carries the original
///     payment outcome (Paid / Pending / Failed); the reversal row carries the later state change.
/// </summary>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackOfficeInvoiceRowKind
{
    Invoice,
    CreditNote,
    Refund
}

/// <summary>
///     Filter values exposed by the back-office invoices toolbar. <see cref="Paid" />, <see cref="Failed" />,
///     and <see cref="Pending" /> match Invoice rows by their original payment outcome.
///     <see cref="Refunded" /> matches RowKind=Refund rows (refund-without-credit-note edge case).
///     <see cref="HasCreditNote" /> matches RowKind=CreditNote rows. The "Refunds and credit notes" UI
///     toggle sends both <see cref="Refunded" /> and <see cref="HasCreditNote" />.
/// </summary>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackOfficeInvoiceStatusFilter
{
    Paid,
    Refunded,
    Failed,
    Pending,
    HasCreditNote
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortableBackOfficeInvoiceProperties
{
    Date,
    TenantName,
    Total,
    Status
}
