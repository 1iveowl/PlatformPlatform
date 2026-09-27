using Account.Client;
using Account.Features.BackOffice.BillingDrift.Queries;
using Blazor.Client.BackOffice.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.BackOffice;

// The back office's billing banners show only with the subscription setting on and only while their summary reports
// something, link into the list with the matching filter, and are read at most once per 60 seconds; the poller stops when
// the page that owns it is left.
public sealed class BillingBannersTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WhenTheSubscriptionSettingIsOff_ShouldShowNoBannerWhateverTheSummary()
    {
        // Arrange
        var summary = new BillingBannerSummary(3, 2, new DashboardMrrConsistencySummaryResponse(100, 90, "USD"));

        // Act
        var banners = BillingBannerModel.Create(false, summary);

        // Assert
        banners.Should().BeEmpty();
    }

    [Fact]
    public void Create_WhenEverySummaryReportsSomething_ShouldShowTheThreeBannersInTheReactOrderWithTheirLinks()
    {
        // Arrange
        var summary = new BillingBannerSummary(3, 2, new DashboardMrrConsistencySummaryResponse(100, 90, "USD"));

        // Act
        var banners = BillingBannerModel.Create(true, summary);

        // Assert
        banners.Select(banner => banner.Kind).Should().Equal(BillingBannerKind.UnsyncedAccounts, BillingBannerKind.MrrMismatch, BillingBannerKind.BillingDrift);
        banners[0].Href.Should().Be("/blazor/back-office/accounts?unsynced=true");
        banners[1].Href.Should().Be("/blazor/back-office/billing-events");
        banners[2].Href.Should().Be("/blazor/back-office/accounts?driftDetected=true");
        banners[2].IsAlert.Should().BeTrue();
        banners[0].IsAlert.Should().BeFalse();
        banners[2].Message.Should().Be("3 accounts have billing drift detected.");
        banners[0].Message.Should().Be("2 accounts have not been synced yet. The MRR trend is incomplete.");
        banners[1].Message.Should().Be("Dashboard MRR mismatch: KPI shows USD 100.00, trend latest shows USD 90.00.");
    }

    [Fact]
    public void Create_WhenOneAccountHasDrift_ShouldUseTheSingularMessage()
    {
        // Act
        var banners = BillingBannerModel.Create(true, new BillingBannerSummary(1, 0, null));

        // Assert
        banners.Should().ContainSingle().Which.Message.Should().Be("1 account has billing drift detected.");
    }

    [Theory]
    [InlineData(100, 100, "USD")]
    [InlineData(100, 90, null)]
    public void Create_WhenTheSummariesReportNothing_ShouldShowNoBanner(decimal kpi, decimal trendLatest, string? currency)
    {
        // Arrange
        var summary = new BillingBannerSummary(0, 0, new DashboardMrrConsistencySummaryResponse(kpi, trendLatest, currency));

        // Act
        var banners = BillingBannerModel.Create(true, summary);

        // Assert
        banners.Should().BeEmpty();
    }

    [Fact]
    public void With_WhenAReadFails_ShouldKeepThePreviousReadingOfThatSummary()
    {
        // Arrange
        var previous = new BillingBannerSummary(3, 2, null);
        var failed = ApiCallResult<BillingDriftSummaryResponse>.Failed(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, null, new Dictionary<string, string[]>(), null));

        // Act
        var summary = previous.With(failed, ApiCallResult<UnsyncedSubscriptionsSummaryResponse>.Success(new UnsyncedSubscriptionsSummaryResponse(0)),
            ApiCallResult<DashboardMrrConsistencySummaryResponse>.Success(new DashboardMrrConsistencySummaryResponse(5, 5, "USD"))
        );

        // Assert
        summary.Should().Be(new BillingBannerSummary(3, 0, new DashboardMrrConsistencySummaryResponse(5, 5, "USD")));
    }

    [Fact]
    public async Task RunAsync_WhenNothingWasRead_ShouldReadAtOnceAndThenWaitTheInterval()
    {
        // Arrange
        var state = new BillingBannerState();
        var clock = new FixedTimeProvider(StartTime);
        var delays = new ControlledDelays();
        var reads = 0;
        using var poller = new BillingBannerPoller(state, clock, delays.DelayAsync, _ =>
            {
                reads++;
                return Task.FromResult(new BillingBannerSummary(reads, 0, null));
            }
        );

        // Act
        var running = poller.RunAsync();
        await delays.WaitForRequestsAsync(1);

        // Assert
        reads.Should().Be(1);
        state.Summary.DriftCount.Should().Be(1);
        delays.Requested.Should().Equal(TimeSpan.FromSeconds(60));
        running.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenTheIntervalHasPassed_ShouldReadAgain()
    {
        // Arrange
        var state = new BillingBannerState();
        var clock = new FixedTimeProvider(StartTime);
        var delays = new ControlledDelays();
        var reads = 0;
        using var poller = new BillingBannerPoller(state, clock, delays.DelayAsync, _ => Task.FromResult(new BillingBannerSummary(++reads, 0, null)));
        var running = poller.RunAsync();
        await delays.WaitForRequestsAsync(1);

        // Act
        clock.Now = StartTime.AddSeconds(60);
        delays.CompleteLatest();
        await delays.WaitForRequestsAsync(2);

        // Assert
        reads.Should().Be(2);
        state.Summary.DriftCount.Should().Be(2);
        delays.Requested.Should().Equal(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        running.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenAnotherPageReadTwentySecondsAgo_ShouldShowThatReadingAndWaitTheRestOfTheInterval()
    {
        // Arrange
        var state = new BillingBannerState();
        state.MarkRead(StartTime.AddSeconds(-20));
        state.Apply(new BillingBannerSummary(4, 0, null));
        var delays = new ControlledDelays();
        var reads = 0;
        using var poller = new BillingBannerPoller(state, new FixedTimeProvider(StartTime), delays.DelayAsync, previous =>
            {
                reads++;
                return Task.FromResult(previous);
            }
        );

        // Act
        _ = poller.RunAsync();
        await delays.WaitForRequestsAsync(1);

        // Assert
        reads.Should().Be(0);
        state.Summary.DriftCount.Should().Be(4);
        delays.Requested.Should().Equal(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public async Task Dispose_WhenThePageIsLeft_ShouldEndTheWaitAndReadNoMore()
    {
        // Arrange
        var state = new BillingBannerState();
        var clock = new FixedTimeProvider(StartTime);
        var delays = new ControlledDelays();
        var reads = 0;
        var poller = new BillingBannerPoller(state, clock, delays.DelayAsync, previous =>
            {
                reads++;
                return Task.FromResult(previous);
            }
        );
        var running = poller.RunAsync();
        await delays.WaitForRequestsAsync(1);

        // Act
        poller.Dispose();
        clock.Now = StartTime.AddMinutes(10);
        await running;

        // Assert
        running.IsCompletedSuccessfully.Should().BeTrue();
        reads.Should().Be(1);
        delays.Requested.Should().ContainSingle();
    }

    [Fact]
    public void State_WhenAReadingIsApplied_ShouldNotifyItsSubscribers()
    {
        // Arrange
        var state = new BillingBannerState();
        var notifications = 0;
        state.Changed += () => notifications++;

        // Act
        state.Apply(new BillingBannerSummary(1, 1, null));

        // Assert
        notifications.Should().Be(1);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    // Each wait completes only when the test says so, or is canceled with its token as Task.Delay is
    private sealed class ControlledDelays
    {
        private readonly Lock _lock = new();
        private readonly List<TaskCompletionSource> _pending = [];
        private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];

        public List<TimeSpan> Requested { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            lock (_lock)
            {
                Requested.Add(delay);
                _pending.Add(completion);
                foreach (var waiter in _waiters.Where(waiter => Requested.Count >= waiter.Count))
                {
                    waiter.Signal.TrySetResult();
                }
            }

            return completion.Task;
        }

        // Completes once the poller has asked for the given number of waits, which is when it has finished the work before them
        public Task WaitForRequestsAsync(int count)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                if (Requested.Count >= count)
                {
                    signal.TrySetResult();
                }
                else
                {
                    _waiters.Add((count, signal));
                }
            }

            return signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void CompleteLatest()
        {
            lock (_lock)
            {
                _pending[^1].TrySetResult();
            }
        }
    }
}
