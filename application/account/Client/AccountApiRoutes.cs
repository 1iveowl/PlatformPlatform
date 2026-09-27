using System.Globalization;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.EmailAuthentication.Domain;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.PushNotifications.Domain;
using Account.Features.Tenants.BackOffice.Requests;
using Account.Features.Users.Requests;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Domain;

namespace Account.Client;

// Every account API path the typed clients call, as mapped in account/Api/Endpoints. Route values and query values are
// URI-escaped here, so no caller builds an account API URL.
public static class AccountApiRoutes
{
    public const string Bootstrap = "/api/account/bootstrap";

    public const string BackOfficeMe = "/api/back-office/me";

    public const string Logout = "/api/account/authentication/logout";

    public const string SwitchTenant = "/api/account/authentication/switch-tenant";

    public const string Sessions = "/api/account/authentication/sessions";

    public const string StartEmailLogin = "/api/account/authentication/email/login/start";

    public const string StartEmailSignup = "/api/account/authentication/email/signup/start";

    public const string Users = "/api/account/users";

    public const string CurrentUser = "/api/account/users/me";

    public const string BulkDeleteUsers = "/api/account/users/bulk-delete";

    public const string InviteUser = "/api/account/users/invite";

    public const string ChangeTheme = "/api/account/users/me/change-theme";
    public const string ChangeZoomLevel = "/api/account/users/me/change-zoom-level";
    public const string ChangeLocale = "/api/account/users/me/change-locale";

    public const string UpdateAvatar = "/api/account/users/me/update-avatar";

    public const string RemoveAvatar = "/api/account/users/me/remove-avatar";

    public const string PushSubscriptions = "/api/account/users/me/push-subscriptions";

    public const string TestPushNotification = "/api/account/users/me/push-subscriptions/test";

    public const string DeletedUsers = "/api/account/users/deleted";

    public const string BulkPurgeUsers = "/api/account/users/deleted/bulk-purge";

    public const string EmptyRecycleBin = "/api/account/users/deleted/empty-recycle-bin";

    public const string Tenants = "/api/account/tenants";

    public const string CurrentTenant = "/api/account/tenants/current";

    public const string UpdateTenantLogo = "/api/account/tenants/current/update-logo";

    public const string RemoveTenantLogo = "/api/account/tenants/current/remove-logo";

    public const string VerificationStatus = "/api/account/authentication/verification";

    public const string TenantConfigurableFeatureFlags = "/api/account/feature-flags/tenant-configurable";

    public const string UserConfigurableFeatureFlags = "/api/account/feature-flags/user-configurable";

    private const string DateFormat = "yyyy-MM-dd";

    // The back office's billing health summaries, which its banners poll
    public const string BackOfficeBillingDriftSummary = "/api/back-office/billing-drift/summary";

    public const string BackOfficeUnsyncedSubscriptionsSummary = "/api/back-office/billing-drift/unsynced-summary";

    public const string BackOfficeMrrConsistencySummary = "/api/back-office/billing-drift/mrr-consistency-summary";

    // The dashboard's distribution of tenants over subscription plans
    public const string BackOfficeDashboardPlanDistribution = "/api/back-office/dashboard/plan-distribution";

    // The external login and signup starts are document navigations, not typed client calls; the provider is the name of
    // the account API's ExternalProviderType value
    public static string RevokeSession(SessionId sessionId)
    {
        return $"{Sessions}/{Uri.EscapeDataString(sessionId.Value)}";
    }

    public static string PushSubscription(PushSubscriptionId pushSubscriptionId)
    {
        return $"{PushSubscriptions}/{Uri.EscapeDataString(pushSubscriptionId.Value)}";
    }

    // The back-office dashboard's KPI tiles for a period, bound from the query string by the enum member's name
    public static string BackOfficeDashboardKpis(DashboardTrendPeriod period)
    {
        return $"/api/back-office/dashboard/kpis?Period={period}";
    }

    // The dashboard's daily trend of one metric (new tenants, new users or login activity) for a period, with the prior period
    public static string BackOfficeDashboardTrends(DashboardTrendMetric metric, DashboardTrendPeriod period)
    {
        return $"/api/back-office/dashboard/trends?Metric={metric}&Period={period}";
    }

    // The dashboard's monthly recurring revenue trend for a period, with the prior period
    public static string BackOfficeDashboardMrrTrend(DashboardTrendPeriod period)
    {
        return $"/api/back-office/dashboard/mrr-trend?Period={period}";
    }

    // The dashboard's cumulative revenue trend for a period, with the prior period
    public static string BackOfficeDashboardRevenueTrend(DashboardTrendPeriod period)
    {
        return $"/api/back-office/dashboard/revenue-trend?Period={period}";
    }

    // The dashboard's recent activity lists (recent-signups, recent-logins, recent-payments, recent-stripe-events), each
    // limited to the given number of rows
    public static string BackOfficeDashboardRecent(string list, int limit)
    {
        return $"/api/back-office/dashboard/{Uri.EscapeDataString(list)}?Limit={limit.ToString(CultureInfo.InvariantCulture)}";
    }

    // The back office's accounts list. The endpoint binds the query with [AsParameters]: PascalCase names, enum names, one
    // repeated parameter per plan and status, and the boolean filters only when set. The first page omits its offset.
    public static string BackOfficeTenants(GetTenantsQuery query)
    {
        var parameters = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(query.Search)) parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantsQuery.Search), query.Search));
        parameters.AddRange((query.Plans ?? []).Select(plan => new KeyValuePair<string, string>(nameof(GetTenantsQuery.Plans), plan.ToString())));
        parameters.AddRange((query.Statuses ?? []).Select(status => new KeyValuePair<string, string>(nameof(GetTenantsQuery.Statuses), status.ToString())));
        if (query.Unsynced) parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantsQuery.Unsynced), "true"));
        if (query.DriftDetected) parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantsQuery.DriftDetected), "true"));
        parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantsQuery.OrderBy), query.OrderBy.ToString()));
        parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantsQuery.SortOrder), query.SortOrder.ToString()));
        if (query.PageOffset > 0) parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantsQuery.PageOffset), query.PageOffset.ToString(CultureInfo.InvariantCulture)));
        parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantsQuery.PageSize), query.PageSize.ToString(CultureInfo.InvariantCulture)));

        return $"/api/back-office/tenants?{string.Join('&', parameters.Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}"))}";
    }

    // One account of the back office's accounts list; its user counts, users and feature flags are below it
    public static string BackOfficeTenant(TenantId tenantId)
    {
        return $"/api/back-office/tenants/{tenantId.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string BackOfficeTenantUserCounts(TenantId tenantId)
    {
        return $"{BackOfficeTenant(tenantId)}/user-counts";
    }

    // One page of an account's users. The endpoint binds the query with [AsParameters]: PascalCase names, one repeated Roles
    // parameter per role, and the search only when set. The first page omits its offset.
    public static string BackOfficeTenantUsers(TenantId tenantId, GetTenantUsersQuery query)
    {
        var parameters = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(query.Search)) parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantUsersQuery.Search), query.Search));
        parameters.AddRange((query.Roles ?? []).Select(role => new KeyValuePair<string, string>(nameof(GetTenantUsersQuery.Roles), role.ToString())));
        if (query.PageOffset > 0) parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantUsersQuery.PageOffset), query.PageOffset.ToString(CultureInfo.InvariantCulture)));
        parameters.Add(new KeyValuePair<string, string>(nameof(GetTenantUsersQuery.PageSize), query.PageSize.ToString(CultureInfo.InvariantCulture)));

        return $"{BackOfficeTenant(tenantId)}/users?{string.Join('&', parameters.Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}"))}";
    }

    public static string BackOfficeTenantFeatureFlags(TenantId tenantId)
    {
        return $"{BackOfficeTenant(tenantId)}/feature-flags";
    }

    // The flag key is a registry key (lower case kebab-case), escaped here like every other route value
    public static string SetTenantAbInclusionPin(TenantId tenantId)
    {
        return $"/api/back-office/tenants/{tenantId.Value.ToString(CultureInfo.InvariantCulture)}/ab-inclusion-pin";
    }

    public static string SetTenantFeatureFlagOverride(string flagKey)
    {
        return $"/api/account/feature-flags/{Uri.EscapeDataString(flagKey)}/tenant-override";
    }

    public static string SetUserFeatureFlagOverride(string flagKey)
    {
        return $"/api/account/feature-flags/{Uri.EscapeDataString(flagKey)}/user-override";
    }

    public static string StartExternalLogin(string provider)
    {
        return $"/api/account/authentication/{Uri.EscapeDataString(provider)}/login/start";
    }

    public static string StartExternalSignup(string provider)
    {
        return $"/api/account/authentication/{Uri.EscapeDataString(provider)}/signup/start";
    }

    // The edition names the client the verification callback returns to; the account API reads it from the query string only,
    // so the request body stays the one the React edition sends
    public static string StartExternalVerification(ExternalProviderType provider, string edition)
    {
        return $"/api/account/authentication/{provider}/verification/start?Edition={Uri.EscapeDataString(edition)}";
    }

    public static string CompleteEmailLogin(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/login/{Uri.EscapeDataString(emailLoginId.Value)}/complete";
    }

    public static string CompleteEmailSignup(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/signup/{Uri.EscapeDataString(emailLoginId.Value)}/complete";
    }

    public static string ResendEmailLoginCode(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/login/{Uri.EscapeDataString(emailLoginId.Value)}/resend-code";
    }

    public static string ResendEmailSignupCode(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/signup/{Uri.EscapeDataString(emailLoginId.Value)}/resend-code";
    }

    public static string User(UserId userId)
    {
        return $"{Users}/{Uri.EscapeDataString(userId.Value)}";
    }

    public static string ChangeUserRole(UserId userId)
    {
        return $"{User(userId)}/change-user-role";
    }

    public static string RestoreUser(UserId userId)
    {
        return $"{User(userId)}/restore";
    }

    public static string PurgeUser(UserId userId)
    {
        return $"{User(userId)}/purge";
    }

    // Bound with [AsParameters] like the users query; the first page omits PageOffset
    public static string GetDeletedUsers(GetDeletedUsersQuery query)
    {
        var pageOffset = query.PageOffset is { } offset ? $"{nameof(GetDeletedUsersQuery.PageOffset)}={offset.ToString(CultureInfo.InvariantCulture)}&" : "";
        return $"{DeletedUsers}?{pageOffset}{nameof(GetDeletedUsersQuery.PageSize)}={query.PageSize.ToString(CultureInfo.InvariantCulture)}";
    }

    // The endpoint binds the query with [AsParameters], so the parameter names are the PascalCase property names, enums
    // are their names and dates are calendar dates. A null value is omitted.
    public static string GetUsers(GetUsersQuery query)
    {
        KeyValuePair<string, string?>[] parameters =
        [
            new(nameof(GetUsersQuery.Search), query.Search),
            new(nameof(GetUsersQuery.UserRole), query.UserRole?.ToString()),
            new(nameof(GetUsersQuery.UserStatus), query.UserStatus?.ToString()),
            new(nameof(GetUsersQuery.StartDate), query.StartDate?.ToString(DateFormat, CultureInfo.InvariantCulture)),
            new(nameof(GetUsersQuery.EndDate), query.EndDate?.ToString(DateFormat, CultureInfo.InvariantCulture)),
            new(nameof(GetUsersQuery.OrderBy), query.OrderBy.ToString()),
            new(nameof(GetUsersQuery.SortOrder), query.SortOrder.ToString()),
            new(nameof(GetUsersQuery.PageOffset), query.PageOffset?.ToString(CultureInfo.InvariantCulture)),
            new(nameof(GetUsersQuery.PageSize), query.PageSize.ToString(CultureInfo.InvariantCulture))
        ];

        var queryString = string.Join('&', parameters.Where(parameter => parameter.Value is not null).Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value!)}"));
        return $"{Users}?{queryString}";
    }
}
