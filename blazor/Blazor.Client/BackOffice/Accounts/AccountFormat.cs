// The texts of an account row as the React back office's AccountsTableRow, TenantStatusBadge and MrrCell show them: the status
// the server filters and sorts by, its label, the monthly recurring revenue with the amount a planned change moves it to, the
// dates, and the label of the renewal date, which reads as an expiry once the subscription is canceling or canceled.

using System.Globalization;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Blazor.Client.BackOffice.Dashboard;

namespace Blazor.Client.BackOffice.Accounts;

// The current amount, and the amount after the planned change when there is one
public sealed record AccountMrr(string Current, string? Next);

public static class AccountFormat
{
    // The account API's own reading of the row (GetTenantsHandler.GetStatus), which its status filter and sort use
    public static TenantStatusFilter GetStatus(TenantSummary tenant)
    {
        return GetStatus(tenant.Plan, tenant.PlannedChange, tenant.HasEverSubscribed);
    }

    // The same reading of an account's detail, whose planned change the React back office's AccountDetailHeader derives from
    // the cancellation flag and the scheduled plan
    public static TenantStatusFilter GetStatus(TenantDetailResponse tenant)
    {
        return GetStatus(tenant.Plan, GetPlannedChange(tenant), tenant.HasEverSubscribed);
    }

    public static PlannedSubscriptionChange? GetPlannedChange(TenantDetailResponse tenant)
    {
        if (tenant.CancelAtPeriodEnd) return PlannedSubscriptionChange.Cancellation;
        return tenant.ScheduledPlan is null ? null : PlannedSubscriptionChange.ScheduledPlanChange;
    }

    private static TenantStatusFilter GetStatus(SubscriptionPlan plan, PlannedSubscriptionChange? plannedChange, bool hasEverSubscribed)
    {
        return (plan, plannedChange, hasEverSubscribed) switch
        {
            (_, PlannedSubscriptionChange.Cancellation, _) => TenantStatusFilter.Canceling,
            (_, PlannedSubscriptionChange.ScheduledPlanChange, _) => TenantStatusFilter.Downgrading,
            (not SubscriptionPlan.Basis, _, _) => TenantStatusFilter.Active,
            (_, _, true) => TenantStatusFilter.Canceled,
            _ => TenantStatusFilter.Free
        };
    }

    public static string GetStatusLabel(TenantStatusFilter status)
    {
        return status switch
        {
            TenantStatusFilter.Active => BackOfficeStrings.StatusActive,
            TenantStatusFilter.Downgrading => BackOfficeStrings.StatusDowngrading,
            TenantStatusFilter.Canceling => BackOfficeStrings.StatusCanceling,
            TenantStatusFilter.Canceled => BackOfficeStrings.StatusCanceled,
            TenantStatusFilter.Free => BackOfficeStrings.StatusFree,
            _ => status.ToString()
        };
    }

    // The class of the status badge; the label always carries the status, so the colour is never the only signal
    public static string GetStatusClass(TenantStatusFilter status)
    {
        return $"account-status account-status-{status.ToString().ToLowerInvariant()}";
    }

    // A cancellation moves the amount to zero and a scheduled plan change to the scheduled price; "-" without a currency
    public static AccountMrr GetMrr(TenantSummary tenant)
    {
        return GetMrr(tenant.MonthlyRecurringRevenue, tenant.Currency, tenant.PlannedChange, tenant.ScheduledPriceAmount);
    }

    public static AccountMrr GetMrr(TenantDetailResponse tenant)
    {
        return GetMrr(tenant.MonthlyRecurringRevenue, tenant.Currency, GetPlannedChange(tenant), tenant.ScheduledPriceAmount);
    }

    private static AccountMrr GetMrr(decimal? monthlyRecurringRevenue, string? currency, PlannedSubscriptionChange? plannedChange, decimal? scheduledPriceAmount)
    {
        var current = monthlyRecurringRevenue is { } amount ? DashboardFormat.FormatMoney(amount, currency) : DashboardFormat.Missing;
        var next = (plannedChange, scheduledPriceAmount, currency) switch
        {
            (PlannedSubscriptionChange.Cancellation, _, not null) => DashboardFormat.FormatMoney(0, currency),
            (PlannedSubscriptionChange.ScheduledPlanChange, { } scheduled, not null) => DashboardFormat.FormatMoney(scheduled, currency),
            _ => null
        };
        return new AccountMrr(current, next);
    }

    public static string GetRenewalLabel(TenantSummary tenant)
    {
        return GetRenewalLabel(GetStatus(tenant));
    }

    public static string GetRenewalLabel(TenantStatusFilter status)
    {
        return status switch
        {
            TenantStatusFilter.Canceled => BackOfficeStrings.Expired,
            TenantStatusFilter.Canceling => BackOfficeStrings.Expires,
            _ => BackOfficeStrings.RenewalDate
        };
    }

    public static string FormatDate(DateTimeOffset? date)
    {
        return date is { } value ? value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : DashboardFormat.Missing;
    }

    public static string? GetOwnerName(TenantSummary tenant)
    {
        return tenant.Owner is { } owner ? DashboardFormat.GetPersonName(owner.FirstName, owner.LastName, owner.Email) : null;
    }

    public static string GetCountry(TenantSummary tenant)
    {
        return string.IsNullOrWhiteSpace(tenant.Country) ? DashboardFormat.Missing : tenant.Country;
    }
}
