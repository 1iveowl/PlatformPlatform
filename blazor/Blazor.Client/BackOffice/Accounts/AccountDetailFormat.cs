// The texts of an account's detail as the React back office's AccountDetailHeader, AccountHealthTiles, AccountOverviewTab and
// CurrentPlanDetails show them: the state and A/B inclusion pin badges, the user count breakdown, the lifetime value, whether
// the account ever had a paid plan, the billing address lines and the payment method.

using System.Globalization;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Tenants.Domain;
using Blazor.Client.BackOffice.Dashboard;
using SharedKernel.FeatureFlags;

namespace Blazor.Client.BackOffice.Accounts;

// The users counted as active, as inactive (neither active nor pending) and as pending
public sealed record AccountUserBreakdown(int Total, int Active, int Inactive, int Pending);

// The brand, the masked number and the expiry of a payment method; the number and the expiry are null where they carry no data
public sealed record AccountPaymentMethod(string Brand, string? MaskedNumber, string? Expiry);

public static class AccountDetailFormat
{
    // The state badge shows only for an account that is not active, as in the React header
    public static string? GetStateLabel(TenantState state)
    {
        return state == TenantState.Active ? null : BackOfficeStrings.TenantStateSuspended;
    }

    public static string? GetAbInclusionPinLabel(AbInclusionPin? pin)
    {
        return pin switch
        {
            AbInclusionPin.AlwaysOn => BackOfficeStrings.FirstInRollouts,
            AbInclusionPin.NeverOn => BackOfficeStrings.LastInRollouts,
            _ => null
        };
    }

    public static AccountUserBreakdown GetUserBreakdown(TenantUserCountsResponse counts)
    {
        return new AccountUserBreakdown(counts.TotalUsers, counts.ActiveUsers, Math.Max(0, counts.TotalUsers - counts.ActiveUsers - counts.PendingUsers), counts.PendingUsers);
    }

    public static string GetLifetimeValue(TenantDetailResponse tenant)
    {
        return tenant.LifetimeValue is { } amount ? DashboardFormat.FormatMoney(amount, tenant.Currency) : DashboardFormat.Missing;
    }

    // The current plan card shows its empty state for an account that never subscribed
    public static bool IsFree(TenantDetailResponse tenant)
    {
        return tenant.SubscribedSince is null && !tenant.HasEverSubscribed;
    }

    // Name, lines, postal code with city, and state, in the React card's order; blank parts are left out
    public static IReadOnlyList<string> GetBillingAddressLines(TenantDetailResponse tenant)
    {
        if (tenant.BillingAddress is not { } address) return [];

        var postalCodeAndCity = string.Join(' ', new[] { address.PostalCode, address.City }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        return new[] { address.Line1, address.Line2, postalCodeAndCity, address.State }.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line!.Trim()).ToArray();
    }

    public static string? GetCountry(TenantDetailResponse tenant)
    {
        return string.IsNullOrWhiteSpace(tenant.BillingAddress?.Country) ? null : tenant.BillingAddress.Country.Trim();
    }

    public static bool HasBillingDetails(TenantDetailResponse tenant)
    {
        return !string.IsNullOrWhiteSpace(tenant.BillingName) || GetBillingAddressLines(tenant).Count > 0 || GetCountry(tenant) is not null || !string.IsNullOrWhiteSpace(tenant.TaxId);
    }

    // A Link payment carries the placeholder number "****" and no expiry, so only its brand is shown, as in the React card
    public static AccountPaymentMethod? GetPaymentMethod(TenantDetailResponse tenant)
    {
        if (tenant.PaymentMethod is not { } paymentMethod) return null;

        var brand = paymentMethod.Brand.Length == 0 ? paymentMethod.Brand : string.Concat(paymentMethod.Brand[..1].ToUpperInvariant(), paymentMethod.Brand[1..]);
        if (string.Equals(paymentMethod.Brand, "link", StringComparison.OrdinalIgnoreCase)) return new AccountPaymentMethod(brand, null, null);

        var expiry = paymentMethod is { ExpMonth: > 0, ExpYear: > 0 }
            ? $"{paymentMethod.ExpMonth.ToString("00", CultureInfo.InvariantCulture)}/{(paymentMethod.ExpYear % 100).ToString("00", CultureInfo.InvariantCulture)}"
            : null;
        return new AccountPaymentMethod(brand, $"•••• {paymentMethod.Last4}", expiry);
    }

    public static string FormatWithDate(string format, DateTimeOffset date)
    {
        return string.Format(CultureInfo.CurrentCulture, format, AccountFormat.FormatDate(date));
    }

    public static string FormatCount(string format, int count)
    {
        return string.Format(CultureInfo.CurrentCulture, format, DashboardFormat.FormatCount(count));
    }

    // The plan a plan flag requires, by its label when the value names a plan and as the API sent it otherwise
    public static string GetRequiredPlanLabel(string requiredPlan)
    {
        var plan = Enum.GetValues<SubscriptionPlan>().Cast<SubscriptionPlan?>().FirstOrDefault(candidate => string.Equals(candidate.ToString(), requiredPlan, StringComparison.OrdinalIgnoreCase));
        return plan is { } named ? DashboardFormat.GetPlanLabel(named) : requiredPlan;
    }
}
