using JetBrains.Annotations;

namespace Account.Features.BackOffice.BillingDrift.Queries;

// The back office's billing health summaries as /api/back-office/billing-drift returns them, which its banners read: the
// subscriptions with detected drift, the paid subscriptions without any billing event yet, and the dashboard's KPI MRR beside
// the latest MRR of its trend. The query records stay with their handlers in the account API.

[PublicAPI]
public sealed record BillingDriftSummaryResponse(int SubscriptionsWithDriftCount);

[PublicAPI]
public sealed record UnsyncedSubscriptionsSummaryResponse(int UnsyncedSubscriptionsCount);

[PublicAPI]
public sealed record DashboardMrrConsistencySummaryResponse(decimal KpiMonthlyRecurringRevenue, decimal TrendLatestMonthlyRecurringRevenue, string? Currency);
