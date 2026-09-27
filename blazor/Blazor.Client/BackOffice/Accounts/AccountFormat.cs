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
        return tenant switch
        {
            { PlannedChange: PlannedSubscriptionChange.Cancellation } => TenantStatusFilter.Canceling,
            { PlannedChange: PlannedSubscriptionChange.ScheduledPlanChange } => TenantStatusFilter.Downgrading,
            { Plan: not SubscriptionPlan.Basis } => TenantStatusFilter.Active,
            { HasEverSubscribed: true } => TenantStatusFilter.Canceled,
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
        var current = tenant.MonthlyRecurringRevenue is { } amount ? DashboardFormat.FormatMoney(amount, tenant.Currency) : DashboardFormat.Missing;
        var next = tenant switch
        {
            { PlannedChange: PlannedSubscriptionChange.Cancellation, Currency: not null } => DashboardFormat.FormatMoney(0, tenant.Currency),
            { PlannedChange: PlannedSubscriptionChange.ScheduledPlanChange, ScheduledPriceAmount: { } scheduled, Currency: not null } =>
                DashboardFormat.FormatMoney(scheduled, tenant.Currency),
            _ => null
        };
        return new AccountMrr(current, next);
    }

    public static string GetRenewalLabel(TenantSummary tenant)
    {
        return GetStatus(tenant) switch
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
