// What the back office's invoices and billing events show beside the dashboard's labels: the React back office's plan
// transition of an event (billingEventPlanTransition.ts), the row kind badge of an invoice row, and whether a row is a
// reversal, which the lists strike through.

using Account.Features.BackOffice.Invoices.Queries;
using Account.Features.Subscriptions.Domain;
using Blazor.Client.BackOffice.Dashboard;

namespace Blazor.Client.BackOffice.Billing;

public sealed record PlanTransition(SubscriptionPlan From, SubscriptionPlan To);

public static class BillingFormat
{
    // The events whose meaning includes a plan change; every other event shows no transition (billingEventCategories.ts)
    private static readonly HashSet<BillingEventType> PlanTransitionEventTypes =
    [
        BillingEventType.SubscriptionCreated, BillingEventType.SubscriptionRenewed, BillingEventType.SubscriptionUpgraded,
        BillingEventType.SubscriptionDowngradeScheduled, BillingEventType.SubscriptionDowngradeCancelled, BillingEventType.SubscriptionDowngraded,
        BillingEventType.SubscriptionReactivated, BillingEventType.SubscriptionExpired, BillingEventType.SubscriptionImmediatelyCancelled,
        BillingEventType.SubscriptionSuspended, BillingEventType.SubscriptionCancelled, BillingEventType.PaymentRefunded
    ];

    // A missing from plan is Basis. A cancellation is stored with the cancelled plan as its to plan, so it is shown flipped:
    // from the cancelled plan to Basis, as the React back office shows it.
    public static PlanTransition? GetPlanTransition(BillingEventType type, SubscriptionPlan? fromPlan, SubscriptionPlan? toPlan)
    {
        if (!PlanTransitionEventTypes.Contains(type) || toPlan is not { } to) return null;

        return type == BillingEventType.SubscriptionCancelled ? new PlanTransition(to, SubscriptionPlan.Basis) : new PlanTransition(fromPlan ?? SubscriptionPlan.Basis, to);
    }

    // A credit note or refund row is the reversal of an invoice row; the invoice row always keeps the original outcome
    public static bool IsReversal(BackOfficeInvoiceRowKind rowKind)
    {
        return rowKind is BackOfficeInvoiceRowKind.CreditNote or BackOfficeInvoiceRowKind.Refund;
    }

    public static string GetRowLabel(BackOfficeInvoiceRowKind rowKind, PaymentTransactionStatus status)
    {
        return rowKind switch
        {
            BackOfficeInvoiceRowKind.CreditNote => BackOfficeStrings.CreditNote,
            BackOfficeInvoiceRowKind.Refund => BackOfficeStrings.PaymentRefunded,
            _ => DashboardFormat.GetPaymentStatusLabel(status)
        };
    }

    // The badge class of a row: reversals and pending rows neutral, a paid invoice positive, a failed one negative
    public static string GetRowBadgeClass(BackOfficeInvoiceRowKind rowKind, PaymentTransactionStatus status)
    {
        var tone = IsReversal(rowKind) ? DashboardTone.Neutral : DashboardFormat.GetPaymentStatusTone(status);
        return $"dashboard-badge {DashboardFormat.GetToneClass("dashboard-badge", tone)}";
    }

    // Money with its sign, or the missing mark when the event carries no amount or no currency
    public static string FormatOptionalMoney(decimal? amount, string? currency)
    {
        return amount is { } value && !string.IsNullOrWhiteSpace(currency) ? DashboardFormat.FormatMoney(value, currency) : DashboardFormat.Missing;
    }
}
