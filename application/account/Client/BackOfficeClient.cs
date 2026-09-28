using Account.Features.BackOffice.BillingDrift.Queries;
using Account.Features.BackOffice.BillingEvents.Queries;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.BackOffice.Invoices.Queries;
using Account.Features.BackOffice.Queries;
using Account.Features.BackOffice.Requests;
using Account.Features.ExternalAuthentication.BackOffice.Queries;
using Account.Features.FeatureFlags.Queries;
using Account.Features.Tenants.BackOffice.Commands;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Tenants.BackOffice.Requests;
using Account.Features.Users.BackOffice.Queries;
using Account.Features.Users.BackOffice.Requests;
using SharedKernel.Domain;

namespace Account.Client;

// The back-office API on the back-office host. The registrant gives it its own HttpClient: the back-office identity comes from
// the platform authentication in front of that host, not from the app's session, so the app's handler chain (bootstrap
// antiforgery token, feature flags, version gate, app login on 401) does not apply to it.
public sealed class BackOfficeClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<MeResponse>> GetMeAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<MeResponse>(AccountApiRoutes.BackOfficeMe, cancellationToken);
    }

    public Task<ApiCallResult> SetTenantAbInclusionPinAsync(TenantId tenantId, SetTenantAbInclusionPinCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.SetTenantAbInclusionPin(tenantId), command, cancellationToken);
    }

    public Task<ApiCallResult<ReconcileTenantWithStripeResponse>> ReconcileTenantWithStripeAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<ReconcileTenantWithStripeResponse>(HttpMethod.Post, AccountApiRoutes.ReconcileTenantWithStripe(tenantId), cancellationToken);
    }

    public Task<ApiCallResult<ReplayArchivedTenantStripeEventsResponse>> ReplayArchivedTenantStripeEventsAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<ReplayArchivedTenantStripeEventsResponse>(HttpMethod.Post, AccountApiRoutes.ReplayArchivedTenantStripeEvents(tenantId), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardKpisResponse>> GetDashboardKpisAsync(DashboardTrendPeriod period, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardKpisResponse>(AccountApiRoutes.BackOfficeDashboardKpis(period), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardTrendsResponse>> GetDashboardTrendsAsync(DashboardTrendMetric metric, DashboardTrendPeriod period, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardTrendsResponse>(AccountApiRoutes.BackOfficeDashboardTrends(metric, period), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardMrrTrendResponse>> GetDashboardMrrTrendAsync(DashboardTrendPeriod period, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardMrrTrendResponse>(AccountApiRoutes.BackOfficeDashboardMrrTrend(period), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardRevenueTrendResponse>> GetDashboardRevenueTrendAsync(DashboardTrendPeriod period, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardRevenueTrendResponse>(AccountApiRoutes.BackOfficeDashboardRevenueTrend(period), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardPlanDistributionResponse>> GetDashboardPlanDistributionAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardPlanDistributionResponse>(AccountApiRoutes.BackOfficeDashboardPlanDistribution, cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardRecentSignupsResponse>> GetRecentSignupsAsync(int limit, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardRecentSignupsResponse>(AccountApiRoutes.BackOfficeDashboardRecent("recent-signups", limit), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardRecentLoginsResponse>> GetRecentLoginsAsync(int limit, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardRecentLoginsResponse>(AccountApiRoutes.BackOfficeDashboardRecent("recent-logins", limit), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardRecentPaymentsResponse>> GetRecentPaymentsAsync(int limit, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardRecentPaymentsResponse>(AccountApiRoutes.BackOfficeDashboardRecent("recent-payments", limit), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeDashboardRecentStripeEventsResponse>> GetRecentStripeEventsAsync(int limit, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeDashboardRecentStripeEventsResponse>(AccountApiRoutes.BackOfficeDashboardRecent("recent-stripe-events", limit), cancellationToken);
    }

    public Task<ApiCallResult<TenantsResponse>> GetTenantsAsync(GetTenantsQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<TenantsResponse>(AccountApiRoutes.BackOfficeTenants(query), cancellationToken);
    }

    public Task<ApiCallResult<BillingDriftSummaryResponse>> GetBillingDriftSummaryAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BillingDriftSummaryResponse>(AccountApiRoutes.BackOfficeBillingDriftSummary, cancellationToken);
    }

    public Task<ApiCallResult<UnsyncedSubscriptionsSummaryResponse>> GetUnsyncedSubscriptionsSummaryAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<UnsyncedSubscriptionsSummaryResponse>(AccountApiRoutes.BackOfficeUnsyncedSubscriptionsSummary, cancellationToken);
    }

    public Task<ApiCallResult<DashboardMrrConsistencySummaryResponse>> GetMrrConsistencySummaryAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<DashboardMrrConsistencySummaryResponse>(AccountApiRoutes.BackOfficeMrrConsistencySummary, cancellationToken);
    }

    public Task<ApiCallResult<TenantDetailResponse>> GetTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<TenantDetailResponse>(AccountApiRoutes.BackOfficeTenant(tenantId), cancellationToken);
    }

    public Task<ApiCallResult<TenantUserCountsResponse>> GetTenantUserCountsAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<TenantUserCountsResponse>(AccountApiRoutes.BackOfficeTenantUserCounts(tenantId), cancellationToken);
    }

    public Task<ApiCallResult<TenantUsersResponse>> GetTenantUsersAsync(TenantId tenantId, GetTenantUsersQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<TenantUsersResponse>(AccountApiRoutes.BackOfficeTenantUsers(tenantId, query), cancellationToken);
    }

    public Task<ApiCallResult<TenantPaymentHistoryResponse>> GetTenantPaymentHistoryAsync(TenantId tenantId, GetTenantPaymentHistoryQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<TenantPaymentHistoryResponse>(AccountApiRoutes.BackOfficeTenantPaymentHistory(tenantId, query), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeInvoicesResponse>> GetInvoicesAsync(GetBackOfficeInvoicesQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeInvoicesResponse>(AccountApiRoutes.BackOfficeInvoices(query), cancellationToken);
    }

    public Task<ApiCallResult<BillingEventsResponse>> GetBillingEventsAsync(GetBackOfficeBillingEventsQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BillingEventsResponse>(AccountApiRoutes.BackOfficeBillingEvents(query), cancellationToken);
    }

    public Task<ApiCallResult<GetTenantFeatureFlagsResponse>> GetTenantFeatureFlagsAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<GetTenantFeatureFlagsResponse>(AccountApiRoutes.BackOfficeTenantFeatureFlags(tenantId), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeUsersResponse>> GetUsersAsync(GetBackOfficeUsersQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeUsersResponse>(AccountApiRoutes.BackOfficeUsers(query), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeUserDetailResponse>> GetUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeUserDetailResponse>(AccountApiRoutes.BackOfficeUser(userId), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeUserSessionsResponse>> GetUserSessionsAsync(UserId userId, GetBackOfficeUserSessionsQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeUserSessionsResponse>(AccountApiRoutes.BackOfficeUserSessions(userId, query), cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeUserLoginHistoryResponse>> GetUserLoginHistoryAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeUserLoginHistoryResponse>(AccountApiRoutes.BackOfficeUserLoginHistory(userId), cancellationToken);
    }

    public Task<ApiCallResult<GetUserFeatureFlagsResponse>> GetUserFeatureFlagsAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<GetUserFeatureFlagsResponse>(AccountApiRoutes.BackOfficeUserFeatureFlags(userId), cancellationToken);
    }

    public Task<ApiCallResult> SetUserAbInclusionPinAsync(UserId userId, SetUserAbInclusionPinCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.SetUserAbInclusionPin(userId), command, cancellationToken);
    }

    public Task<ApiCallResult<BackOfficeUserIdentityVerificationResponse>> GetUserIdentityVerificationAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BackOfficeUserIdentityVerificationResponse>(AccountApiRoutes.BackOfficeUserIdentityVerification(userId), cancellationToken);
    }

    public Task<ApiCallResult> RevokeUserIdentityVerificationAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Delete, AccountApiRoutes.BackOfficeUserIdentityVerification(userId), cancellationToken);
    }
}
