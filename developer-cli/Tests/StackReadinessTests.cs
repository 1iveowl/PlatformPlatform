using System.Net;
using DeveloperCli.Commands;
using FluentAssertions;

namespace DeveloperCli.Tests;

public sealed class StackReadinessTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void WaitUntilReady_WhenEveryResourceIsReadyInOneSnapshot_ShouldReturnReadyWithExitCodeZero()
    {
        // Arrange
        var snapshots = new[] { Snapshot(accountWorkersListening: false), Snapshot() };

        // Act
        var result = Wait(snapshots);

        // Assert
        result.Outcome.Should().Be(ReadinessOutcome.Ready);
        result.ExitCode.Should().Be(0);
        result.Elapsed.Should().Be(PollInterval * 2);
    }

    [Fact]
    public void WaitUntilReady_WhenGatewayAnswers500_ShouldTimeOutWithNonZeroExitCodeAndNameTheGateway()
    {
        // Arrange
        var snapshots = new[] { Snapshot(gatewayStatus: HttpStatusCode.InternalServerError) };

        // Act
        var result = Wait(snapshots);

        // Assert
        result.Outcome.Should().Be(ReadinessOutcome.TimedOut);
        result.ExitCode.Should().Be(1);
        StackReadiness.DescribeFailure(result, 30).Should().Equal(
            "The stack was not ready within 30s.",
            "app-gateway (https://app.dev.localhost:9000/): pending, HTTP 500 InternalServerError, expected 200 OK"
        );
    }

    [Fact]
    public void WaitUntilReady_WhenWorkerProcessExits_ShouldFailAtOnceWithNonZeroExitCode()
    {
        // Arrange
        var snapshots = new[] { Snapshot(accountWorkersListening: false), Snapshot(accountWorkersExited: true), Snapshot() };

        // Act
        var result = Wait(snapshots);

        // Assert
        result.Outcome.Should().Be(ReadinessOutcome.ResourceFailed);
        result.ExitCode.Should().Be(1);
        result.Elapsed.Should().Be(PollInterval * 2);
        StackReadiness.DescribeFailure(result, 30).Should().Contain("account-workers (tcp://localhost:9015): failed, the process exited");
    }

    [Fact]
    public void WaitUntilReady_WhenResourceBecomesUnhealthyAfterEarlierSuccess_ShouldNotReportReady()
    {
        // Arrange: each resource is ready in some poll, but never all of them in the same poll
        var snapshots = new[]
        {
            Snapshot(gatewayStatus: HttpStatusCode.BadGateway),
            Snapshot(accountApiStatus: HttpStatusCode.ServiceUnavailable)
        };

        // Act
        var result = Wait(snapshots);

        // Assert
        result.Outcome.Should().Be(ReadinessOutcome.TimedOut);
        result.ExitCode.Should().Be(1);
        StackReadiness.DescribeFailure(result, 30).Should().Contain(line => line.StartsWith("account-api (") && line.Contains("HTTP 503"));
    }

    [Fact]
    public void WaitUntilReady_WhenWorkerStopsListeningAfterEarlierSuccess_ShouldNotReportReady()
    {
        // Arrange
        var snapshots = new[] { Snapshot(gatewayStatus: null), Snapshot(accountWorkersListening: false) };

        // Act
        var result = Wait(snapshots);

        // Assert
        result.Outcome.Should().Be(ReadinessOutcome.TimedOut);
        StackReadiness.DescribeFailure(result, 30).Should().Contain(line => line.StartsWith("account-workers (") && line.Contains("not listening"));
    }

    [Fact]
    public void WaitUntilReady_WhenAppHostExitsAfterStarting_ShouldFailWithNonZeroExitCode()
    {
        // Arrange
        var snapshots = new[] { Snapshot(gatewayStatus: null), Snapshot(appHostRunning: false) };

        // Act
        var result = Wait(snapshots);

        // Assert
        result.Outcome.Should().Be(ReadinessOutcome.AppHostExited);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public void ObserveHttp_WhenNoResponse_ShouldBePending()
    {
        // Act
        var observation = StackReadiness.ObserveHttp("main-api", "https://localhost:9010/internal-api/ready", HttpStatusCode.OK, null);

        // Assert
        observation.State.Should().Be(ResourceState.Pending);
        observation.Detail.Should().Be("no response");
    }

    [Fact]
    public void ObserveHttp_WhenRedirectInsteadOfDocumentedSuccess_ShouldBePending()
    {
        // Act
        var observation = StackReadiness.ObserveHttp("app-gateway", "https://app.dev.localhost:9000/", HttpStatusCode.OK, HttpStatusCode.Found);

        // Assert
        observation.State.Should().Be(ResourceState.Pending);
    }

    [Fact]
    public void ProcessWatch_WhenProcessWasSeenAndIsGone_ShouldReportExit()
    {
        // Arrange
        var processWatch = new ProcessWatch();

        // Act
        var exitedBeforeStart = processWatch.HasExited(false, false);
        var exitedWhileRunning = processWatch.HasExited(true, false);
        var exitedAfterRunning = processWatch.HasExited(false, false);

        // Assert
        exitedBeforeStart.Should().BeFalse();
        exitedWhileRunning.Should().BeFalse();
        exitedAfterRunning.Should().BeTrue();
    }

    [Fact]
    public void ProcessWatch_WhenProcessWasNeverSeenButDependentApiAnswers_ShouldReportExit()
    {
        // Arrange: the worker started and died between two polls, and the API that waits for it has started
        var processWatch = new ProcessWatch();

        // Act
        var exited = processWatch.HasExited(false, true);

        // Assert
        exited.Should().BeTrue();
    }

    [Fact]
    public void ProcessWatch_WhenRunningWhileDependentApiAnswers_ShouldNotReportExit()
    {
        // Arrange
        var processWatch = new ProcessWatch();

        // Act
        var exited = processWatch.HasExited(true, true);

        // Assert
        exited.Should().BeFalse();
    }

    private static ReadinessResult Wait(ReadinessSnapshot[] snapshots)
    {
        var elapsed = TimeSpan.Zero;
        var poll = 0;
        return StackReadiness.WaitUntilReady(
            () => snapshots[Math.Min(poll++, snapshots.Length - 1)],
            Timeout,
            PollInterval,
            () => elapsed,
            interval => elapsed += interval
        );
    }

    private static ReadinessSnapshot Snapshot(
        bool appHostRunning = true,
        bool accountWorkersListening = true,
        bool accountWorkersExited = false,
        HttpStatusCode? accountApiStatus = HttpStatusCode.OK,
        HttpStatusCode? gatewayStatus = HttpStatusCode.OK
    )
    {
        return new ReadinessSnapshot(appHostRunning, [
                StackReadiness.ObserveWorker("account-workers", 9015, accountWorkersListening, accountWorkersExited),
                StackReadiness.ObserveWorker("main-workers", 9012, true, false),
                StackReadiness.ObserveHttp("account-api", "https://localhost:9013/internal-api/ready", HttpStatusCode.OK, accountApiStatus),
                StackReadiness.ObserveHttp("main-api", "https://localhost:9010/internal-api/ready", HttpStatusCode.OK, HttpStatusCode.OK),
                StackReadiness.ObserveHttp("app-gateway", "https://app.dev.localhost:9000/", HttpStatusCode.OK, gatewayStatus)
            ]
        );
    }
}
