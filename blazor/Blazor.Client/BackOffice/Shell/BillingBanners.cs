// The back office's three billing banners, the React back office's UnsyncedAccountsBanner, MrrMismatchBanner and
// BillingDriftBanner in that order: paid subscriptions without a billing event yet, the dashboard's KPI MRR disagreeing with
// the latest MRR of its trend, and subscriptions with detected drift. They show only with the subscription setting on and only
// while their summary reports something, and each links into the list that shows the cause.
//
// The summaries are read at most once per poll interval, the React back office's 60 seconds, per runtime: BillingBannerState
// is one per runtime, so an enhanced navigation to another back-office page shows the last summaries and waits for the rest of
// the interval instead of reading again. A poller belongs to the banner component on one page and stops when that page is left.

using System.Globalization;
using Account.Client;
using Account.Features.BackOffice.BillingDrift.Queries;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Dashboard;

namespace Blazor.Client.BackOffice.Shell;

public enum BillingBannerKind
{
    UnsyncedAccounts,
    MrrMismatch,
    BillingDrift
}

// A shown banner. An alert interrupts a screen reader (the drift banner, as in the React back office); a status waits.
public sealed record BillingBanner(
    BillingBannerKind Kind,
    string Message,
    string LinkText,
    string Href,
    bool IsAlert,
    string TestId
);

// The last successful reading of each summary; a failed read keeps the previous one, as the React back office's query does
public sealed record BillingBannerSummary(int? DriftCount, int? UnsyncedCount, DashboardMrrConsistencySummaryResponse? MrrConsistency)
{
    public static readonly BillingBannerSummary Empty = new(null, null, null);

    public BillingBannerSummary With(
        ApiCallResult<BillingDriftSummaryResponse> drift,
        ApiCallResult<UnsyncedSubscriptionsSummaryResponse> unsynced,
        ApiCallResult<DashboardMrrConsistencySummaryResponse> mrrConsistency)
    {
        return new BillingBannerSummary(
            drift.IsSuccess ? drift.Value.SubscriptionsWithDriftCount : DriftCount,
            unsynced.IsSuccess ? unsynced.Value.UnsyncedSubscriptionsCount : UnsyncedCount,
            mrrConsistency.IsSuccess ? mrrConsistency.Value : MrrConsistency
        );
    }

    public static async Task<BillingBannerSummary> ReadAsync(BackOfficeClient backOfficeClient, BillingBannerSummary previous)
    {
        var drift = backOfficeClient.GetBillingDriftSummaryAsync(CancellationToken.None);
        var unsynced = backOfficeClient.GetUnsyncedSubscriptionsSummaryAsync(CancellationToken.None);
        var mrrConsistency = backOfficeClient.GetMrrConsistencySummaryAsync(CancellationToken.None);
        return previous.With(await drift, await unsynced, await mrrConsistency);
    }
}

public static class BillingBannerModel
{
    public static IReadOnlyList<BillingBanner> Create(bool isSubscriptionEnabled, BillingBannerSummary summary)
    {
        if (!isSubscriptionEnabled) return [];

        var banners = new List<BillingBanner>();
        if (summary.UnsyncedCount is > 0 and var unsyncedCount)
        {
            var message = unsyncedCount == 1 ? BackOfficeStrings.UnsyncedAccountsOne : BackOfficeStrings.UnsyncedAccountsOther;
            banners.Add(new BillingBanner(BillingBannerKind.UnsyncedAccounts, string.Format(CultureInfo.CurrentCulture, message, unsyncedCount), BackOfficeStrings.ViewAccounts,
                    AccountsListSource.ToUrl(new Dictionary<string, string> { [AccountsListSource.UnsyncedParameter] = "true" }), false, "unsynced-accounts-banner"
                )
            );
        }

        if (summary.MrrConsistency is { Currency: { } currency } consistency && consistency.KpiMonthlyRecurringRevenue != consistency.TrendLatestMonthlyRecurringRevenue)
        {
            var message = string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.MrrMismatch, DashboardFormat.FormatMoney(consistency.KpiMonthlyRecurringRevenue, currency),
                DashboardFormat.FormatMoney(consistency.TrendLatestMonthlyRecurringRevenue, currency)
            );
            banners.Add(new BillingBanner(BillingBannerKind.MrrMismatch, message, BackOfficeStrings.ViewBillingEvents, BackOfficeUrls.ToAbsolute("billing-events"), false, "mrr-mismatch-banner"));
        }

        if (summary.DriftCount is > 0 and var driftCount)
        {
            var message = driftCount == 1 ? BackOfficeStrings.AccountsWithDriftOne : BackOfficeStrings.AccountsWithDriftOther;
            banners.Add(new BillingBanner(BillingBannerKind.BillingDrift, string.Format(CultureInfo.CurrentCulture, message, driftCount), BackOfficeStrings.ViewAccounts,
                    AccountsListSource.ToUrl(new Dictionary<string, string> { [AccountsListSource.DriftDetectedParameter] = "true" }), true, "billing-drift-banner"
                )
            );
        }

        return banners;
    }
}

// The runtime's last summaries and when they were last read, shared by the banner component of every back-office page
public sealed class BillingBannerState
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private DateTimeOffset? _lastReadAt;

    public BillingBannerSummary Summary { get; private set; } = BillingBannerSummary.Empty;

    public event Action? Changed;

    public TimeSpan GetTimeUntilNextRead(DateTimeOffset now)
    {
        if (_lastReadAt is not { } lastReadAt) return TimeSpan.Zero;
        var remaining = lastReadAt + PollInterval - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    // Marked when a read starts, so a page that starts while another page's read is in flight waits for its result
    public void MarkRead(DateTimeOffset now)
    {
        _lastReadAt = now;
    }

    public void Apply(BillingBannerSummary summary)
    {
        Summary = summary;
        Changed?.Invoke();
    }
}

// Reads the summaries whenever the state says a read is due and waits in between. Dispose stops it: a pending wait ends at
// once, and a read in flight still updates the state but starts no further wait.
public sealed class BillingBannerPoller(
    BillingBannerState state,
    TimeProvider timeProvider,
    Func<TimeSpan, CancellationToken, Task> delay,
    Func<BillingBannerSummary, Task<BillingBannerSummary>> read
) : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    public async Task RunAsync()
    {
        var stopping = _stopping.Token;
        while (!stopping.IsCancellationRequested)
        {
            var wait = state.GetTimeUntilNextRead(timeProvider.GetUtcNow());
            if (wait > TimeSpan.Zero)
            {
                await delay(wait, stopping).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                continue;
            }

            state.MarkRead(timeProvider.GetUtcNow());
            state.Apply(await read(state.Summary));
        }
    }
}
