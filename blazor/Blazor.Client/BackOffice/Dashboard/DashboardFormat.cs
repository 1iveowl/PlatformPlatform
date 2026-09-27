// The back-office dashboard's texts and tiles, as the React back office's dashboard shows them: the period in days, the
// KPI tiles the subscription setting allows with their links, counts and money in the current culture, the delta against
// the prior period with its sign, and the labels of plans, payment statuses and billing event types. A value the API left
// out renders as "-".

using System.Globalization;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.Subscriptions.Domain;

namespace Blazor.Client.BackOffice.Dashboard;

public enum DashboardTone
{
    Neutral,
    Positive,
    Negative
}

// One KPI tile: Value is null while the KPIs load; a tile with an Href links to the list it summarizes
public sealed record DashboardTile(
    string TestId,
    string Label,
    string? Value,
    string? Subtitle,
    string? Href,
    string? Delta = null,
    DashboardTone DeltaTone = DashboardTone.Neutral
);

public static class DashboardFormat
{
    public const int RecentLimit = 6;

    public const string Missing = "-";

    public static readonly DashboardTrendPeriod[] Periods = [DashboardTrendPeriod.Last7Days, DashboardTrendPeriod.Last30Days, DashboardTrendPeriod.Last90Days];

    public static int GetDays(DashboardTrendPeriod period)
    {
        return period switch
        {
            DashboardTrendPeriod.Last7Days => 7,
            DashboardTrendPeriod.Last90Days => 90,
            _ => 30
        };
    }

    public static string GetPeriodLabel(DashboardTrendPeriod period)
    {
        return period switch
        {
            DashboardTrendPeriod.Last7Days => BackOfficeStrings.Period7Days,
            DashboardTrendPeriod.Last90Days => BackOfficeStrings.Period90Days,
            _ => BackOfficeStrings.Period30Days
        };
    }

    // The weekday and the day of the month without the year, in the current culture ("Sunday, September 27")
    public static string FormatToday(DateTime today)
    {
        var culture = CultureInfo.CurrentCulture;
        return $"{today.ToString("dddd", culture)}, {today.ToString(culture.DateTimeFormat.MonthDayPattern, culture)}";
    }

    // The KPI tiles in the React back office's order; blended MRR and total revenue only with the subscription setting on
    public static IReadOnlyList<DashboardTile> CreateTiles(BackOfficeDashboardKpisResponse? kpis, DashboardTrendPeriod period, bool isSubscriptionEnabled)
    {
        var days = GetDays(period);
        var tiles = new List<DashboardTile>
        {
            new("kpi-total-accounts", BackOfficeStrings.TotalAccounts, kpis is null ? null : FormatCount(kpis.TotalTenants),
                kpis is null ? null : string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.NewInLastDays, FormatCount(kpis.NewTenantsInPeriod), days),
                BackOfficeUrls.ToAbsolute("accounts")
            )
        };

        if (isSubscriptionEnabled)
        {
            var mrrDelta = kpis?.BlendedMonthlyRecurringRevenueDeltaPercent;
            tiles.Add(new DashboardTile("kpi-blended-mrr", BackOfficeStrings.BlendedMrr, kpis is null ? null : FormatMoney(kpis.BlendedMonthlyRecurringRevenue, kpis.Currency),
                    mrrDelta is null ? null : BackOfficeStrings.VsPriorPeriod, BackOfficeUrls.ToAbsolute("accounts?statuses=Active&statuses=Downgrading"),
                    mrrDelta is null ? null : FormatDeltaPercent(mrrDelta.Value), GetTone(mrrDelta)
                )
            );
            tiles.Add(new DashboardTile("kpi-total-revenue", BackOfficeStrings.TotalRevenue, kpis is null ? null : FormatMoney(kpis.TotalRevenue, kpis.Currency),
                    BackOfficeStrings.AllTimeExcludingVat, BackOfficeUrls.ToAbsolute("invoices")
                )
            );
        }

        tiles.Add(new DashboardTile("kpi-users-active", BackOfficeStrings.UsersActive, kpis is null ? null : FormatCount(kpis.ActiveUsersInPeriod),
                string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.LastDays, days), BackOfficeUrls.ToAbsolute("users")
            )
        );
        tiles.Add(new DashboardTile("kpi-active-sessions", BackOfficeStrings.ActiveSessions, kpis is null ? null : FormatCount(kpis.ActiveSessionsLast24Hours),
                BackOfficeStrings.Last24Hours, null
            )
        );
        return tiles;
    }

    public static string FormatCount(long count)
    {
        return count.ToString("N0", CultureInfo.CurrentCulture);
    }

    // An amount with two decimals in the current culture and the ISO code on the side of the number where the culture puts
    // its currency symbol ("USD 1,234.50", "1.234,50 USD", "-USD 3.00"); "-" without a currency, which the API reports when
    // no platform currency is known
    public static string FormatMoney(decimal amount, string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return Missing;

        var culture = CultureInfo.CurrentCulture;
        var code = currency.ToUpperInvariant();
        var number = Math.Abs(amount).ToString("N2", culture);
        var text = culture.NumberFormat.CurrencyPositivePattern is 1 or 3 ? $"{number} {code}" : $"{code} {number}";
        return amount < 0 ? $"-{text}" : text;
    }

    // One decimal with a sign unless zero, as the React dashboard's DeltaPercent ("+12.5%", "-3.0%", "0.0%")
    public static string FormatDeltaPercent(decimal value)
    {
        var number = Math.Abs(value).ToString("0.0", CultureInfo.CurrentCulture);
        return value switch
        {
            > 0 => $"+{number}%",
            < 0 => $"-{number}%",
            _ => $"{number}%"
        };
    }

    public static DashboardTone GetTone(decimal? value)
    {
        return value switch
        {
            > 0 => DashboardTone.Positive,
            < 0 => DashboardTone.Negative,
            _ => DashboardTone.Neutral
        };
    }

    // A person as the React dashboard names one: the first and last name, else the email
    public static string GetPersonName(string? firstName, string? lastName, string email)
    {
        var name = string.Join(' ', new[] { firstName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        return name.Length > 0 ? name : email;
    }

    public static string GetPlanLabel(SubscriptionPlan plan)
    {
        return plan switch
        {
            SubscriptionPlan.Basis => BackOfficeStrings.PlanBasis,
            SubscriptionPlan.Standard => BackOfficeStrings.PlanStandard,
            SubscriptionPlan.Premium => BackOfficeStrings.PlanPremium,
            _ => plan.ToString()
        };
    }

    public static string GetPaymentStatusLabel(PaymentTransactionStatus status)
    {
        return status switch
        {
            PaymentTransactionStatus.Succeeded => BackOfficeStrings.PaymentPaid,
            PaymentTransactionStatus.Failed => BackOfficeStrings.PaymentFailed,
            PaymentTransactionStatus.Pending => BackOfficeStrings.PaymentPending,
            PaymentTransactionStatus.Refunded => BackOfficeStrings.PaymentRefunded,
            PaymentTransactionStatus.Cancelled => BackOfficeStrings.PaymentCancelled,
            _ => status.ToString()
        };
    }

    public static DashboardTone GetPaymentStatusTone(PaymentTransactionStatus status)
    {
        return status switch
        {
            PaymentTransactionStatus.Succeeded => DashboardTone.Positive,
            PaymentTransactionStatus.Failed => DashboardTone.Negative,
            _ => DashboardTone.Neutral
        };
    }

    public static string GetBillingEventTypeLabel(BillingEventType type)
    {
        return type switch
        {
            BillingEventType.SubscriptionCreated => BackOfficeStrings.EventSubscribed,
            BillingEventType.SubscriptionRenewed => BackOfficeStrings.EventRenewed,
            BillingEventType.SubscriptionUpgraded => BackOfficeStrings.EventUpgraded,
            BillingEventType.SubscriptionDowngradeScheduled => BackOfficeStrings.EventDowngradeScheduled,
            BillingEventType.SubscriptionDowngradeCancelled => BackOfficeStrings.EventDowngradeCancelled,
            BillingEventType.SubscriptionDowngraded => BackOfficeStrings.EventDowngraded,
            BillingEventType.SubscriptionCancelled => BackOfficeStrings.EventCancelled,
            BillingEventType.SubscriptionReactivated => BackOfficeStrings.EventReactivated,
            BillingEventType.SubscriptionExpired => BackOfficeStrings.EventExpired,
            BillingEventType.SubscriptionImmediatelyCancelled => BackOfficeStrings.EventCancelledImmediately,
            BillingEventType.SubscriptionSuspended => BackOfficeStrings.EventSuspended,
            BillingEventType.SubscriptionPastDue => BackOfficeStrings.EventPastDue,
            BillingEventType.PaymentFailed => BackOfficeStrings.EventPaymentFailed,
            BillingEventType.PaymentRecovered => BackOfficeStrings.EventPaymentRecovered,
            BillingEventType.PaymentRefunded => BackOfficeStrings.EventPaymentRefunded,
            BillingEventType.BillingInfoAdded => BackOfficeStrings.EventBillingInfoAdded,
            BillingEventType.BillingInfoUpdated => BackOfficeStrings.EventBillingInfoUpdated,
            BillingEventType.PaymentMethodUpdated => BackOfficeStrings.EventPaymentMethodUpdated,
            BillingEventType.NoOp => BackOfficeStrings.EventNoChange,
            BillingEventType.Unclassified => BackOfficeStrings.EventUnclassified,
            _ => type.ToString()
        };
    }

    public static string GetToneClass(string prefix, DashboardTone tone)
    {
        return $"{prefix}-{tone.ToString().ToLowerInvariant()}";
    }
}
