// The texts of the back office's users as the React back office's UsersTableRow, UserDetailHeader, UserActivityTiles,
// UserTenantsSection, UserLoginHistorySection and UserSessionsSection show them: the person's name, the email confirmation
// badge, the memberships count, an account membership's status and monthly recurring revenue, a sign-in attempt's outcome and
// a session's browser, operating system, last activity and status. Every badge carries its meaning as text, never by colour
// alone.

using System.Globalization;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Users.BackOffice.Queries;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Dashboard;
using Blazor.Client.Sessions;

namespace Blazor.Client.BackOffice.Users;

// A badge's text and its class
public sealed record UserBadge(string Label, string Class);

public static class UserDetailFormat
{
    private const string SuccessClass = "account-status account-status-active";
    private const string WarningClass = "account-status account-status-downgrading";
    private const string NeutralClass = "account-status account-status-free";
    private const string FailureClass = "account-status account-status-canceling";

    public static string GetDisplayName(BackOfficeUserSummary user)
    {
        return DashboardFormat.GetPersonName(user.FirstName, user.LastName, user.Email);
    }

    public static string GetDisplayName(BackOfficeUserDetailResponse user)
    {
        return DashboardFormat.GetPersonName(user.FirstName, user.LastName, user.Email);
    }

    public static string GetActivityLabel(UserActivityFilter activity)
    {
        return activity switch
        {
            UserActivityFilter.ActiveLast24Hours => BackOfficeStrings.Activity24Hours,
            UserActivityFilter.ActiveLast7Days => BackOfficeStrings.Activity7Days,
            UserActivityFilter.ActiveLast30Days => BackOfficeStrings.Activity30Days,
            _ => BackOfficeStrings.ActivityInactive
        };
    }

    public static UserBadge GetEmailBadge(bool emailConfirmed)
    {
        return emailConfirmed ? new UserBadge(BackOfficeStrings.EmailConfirmed, SuccessClass) : new UserBadge(BackOfficeStrings.EmailPending, WarningClass);
    }

    public static string GetMembershipsLabel(int count)
    {
        return AccountDetailFormat.FormatCount(count == 1 ? BackOfficeStrings.MembershipsOne : BackOfficeStrings.MembershipsOther, count);
    }

    public static TenantStatusFilter GetStatus(BackOfficeUserTenantMembership membership)
    {
        return AccountFormat.GetStatus(membership.Plan, membership.PlannedChange, membership.HasEverSubscribed);
    }

    public static AccountMrr GetMrr(BackOfficeUserTenantMembership membership)
    {
        return AccountFormat.GetMrr(membership.MonthlyRecurringRevenue, membership.Currency, membership.PlannedChange, membership.ScheduledPriceAmount);
    }

    // Succeeded, else the failure reason as the account API names it, else pending or failed, as the React outcome badge reads
    public static UserBadge GetOutcome(BackOfficeUserLoginEntry entry)
    {
        if (entry.Outcome == LoginEventOutcome.Succeeded) return new UserBadge(BackOfficeStrings.LoginSucceeded, SuccessClass);
        if (!string.IsNullOrWhiteSpace(entry.FailureReason)) return new UserBadge(entry.FailureReason, WarningClass);
        return entry.Outcome == LoginEventOutcome.Pending ? new UserBadge(UsersStrings.Pending, NeutralClass) : new UserBadge(BackOfficeStrings.LoginFailed, FailureClass);
    }

    public static UserBadge GetSessionStatus(BackOfficeUserSession session)
    {
        return session.RevokedAt is null ? new UserBadge(UsersStrings.Active, SuccessClass) : new UserBadge(BackOfficeStrings.Revoked, FailureClass);
    }

    // The last activity, or the sign-in when the session has not been active since
    public static DateTimeOffset GetLastSeen(BackOfficeUserSession session)
    {
        return session.LastActiveAt ?? session.CreatedAt;
    }

    public static ParsedUserAgent GetUserAgent(BackOfficeUserSession session)
    {
        return UserAgentParser.Parse(session.UserAgent);
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }
}
