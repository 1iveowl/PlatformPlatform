using DeveloperCli.Utilities;
using FluentAssertions;

namespace DeveloperCli.Tests;

public sealed class StackProcessesTests
{
    private const string Worktree = "/workspaces/repo";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    // The process table of a running stack, recorded from a development container on 2026-09-19. The orchestrator (dcp) is
    // started outside the AppHost's process tree, and the AppHost's own executable never names "dotnet".
    private static readonly ProcessSnapshot[] RunningStack =
    [
        new(1, 0, "/sbin/init"),
        new(100, 1, $"script -q -f -c dotnet run --project {Worktree}/application/AppHost/AppHost.csproj {Worktree}/.workspace/developer-cli/aspire-apphost.log"),
        new(101, 100, $"dotnet run --project {Worktree}/application/AppHost/AppHost.csproj"),
        new(102, 101, $"{Worktree}/application/AppHost/bin/Debug/net10.0/AppHost"),
        new(200, 1, "/home/vscode/.nuget/packages/aspire.hosting.orchestration.linux-arm64/13.4.6/tools/dcp start-apiserver --monitor 102 --kubeconfig /tmp/aspire/kubeconfig"),
        new(201, 200, "/home/vscode/.nuget/packages/aspire.hosting.orchestration.linux-arm64/13.4.6/tools/dcp run-controllers --kubeconfig /tmp/aspire/kubeconfig --monitor 200"),
        new(202, 201, "dotnet exec --runtimeconfig /tmp/aspire/runtimeconfig.json /home/vscode/.nuget/packages/aspire.dashboard.sdk.linux-arm64/13.4.6/tools/Aspire.Dashboard.dll"),
        new(300, 201, $"{Worktree}/application/AppGateway/bin/Debug/net10.0/AppGateway"),
        new(400, 201, $"node {Worktree}/application/node_modules/.bin/rsbuild dev"),
        new(500, 1, "docker-proxy -proto tcp -host-ip 0.0.0.0 -host-port 9004 -container-port 5432"),
        // Names the worktree and the AppHost but holds no port, so a port-based resolution must leave it alone.
        new(900, 1, $"bash -c dotnet run --project {Worktree}/application/AppHost/AppHost.csproj")
    ];

    private static readonly ProcessSnapshot[] ExtraProcesses =
    [
        new(701, 700, $"{Worktree}/developer-cli/bin/pp stop"),
        new(950, 1, "python3 -m http.server 9010")
    ];

    private static readonly PortListener[] RunningStackListeners =
    [
        Listener(9000, 300),
        Listener(9003, 201),
        Listener(9004, 500),
        Listener(9008, 201),
        Listener(9009, 102),
        Listener(9011, 400)
    ];

    [Fact]
    public void ResolveOwnership_WhenTheLauncherIsAlive_ShouldOwnTheLauncherTheAppHostAndTheOrchestrator()
    {
        // Act
        var ownership = StackProcesses.ResolveOwnership(RunningStackListeners, RunningStack, Worktree, Protected());

        // Assert
        ownership.Owned.Select(process => process.ProcessId).Should().Equal(100, 101, 102, 200, 201, 202, 300, 400);
        ownership.Foreign.Should().BeEmpty();
        ownership.ContainerPorts.Select(listener => listener.Port).Should().Equal(9004);
    }

    [Fact]
    public void ResolveOwnership_WhenTheAppHostIsOrphanedFromItsLauncher_ShouldStillOwnTheAppHostAndTheOrchestrator()
    {
        // Arrange: the case that failed in practice. The launcher and "dotnet run" are gone, the AppHost is reparented to init,
        // and nothing left in the table matches a pattern built around "dotnet".
        var processes = RunningStack
            .Where(process => process.ProcessId is not (100 or 101))
            .Select(process => process.ProcessId == 102 ? process with { ParentProcessId = 1 } : process)
            .ToArray();

        // Act
        var ownership = StackProcesses.ResolveOwnership(RunningStackListeners, processes, Worktree, Protected());

        // Assert
        ownership.Owned.Select(process => process.ProcessId).Should().Equal(102, 200, 201, 202, 300, 400);
        ownership.Owned.Single(process => process.ProcessId == 102).Reason.Should().Be("holds port 9009");
        ownership.Owned.Single(process => process.ProcessId == 200).Reason.Should().Be("monitors process 102");
    }

    [Fact]
    public void ResolveOwnership_WhenAProcessNamesTheWorktreeButHoldsNoPort_ShouldNotOwnIt()
    {
        // Act
        var ownership = StackProcesses.ResolveOwnership(RunningStackListeners, RunningStack, Worktree, Protected());

        // Assert
        ownership.Owned.Select(process => process.ProcessId).Should().NotContain(900);
    }

    [Fact]
    public void ResolveOwnership_WhenAForeignProcessHoldsAnAllocatedPort_ShouldReportItWithoutOwningIt()
    {
        // Arrange
        var processes = RunningStack.Append(new ProcessSnapshot(950, 1, "python3 -m http.server 9010")).ToArray();
        var listeners = RunningStackListeners.Append(Listener(9010, 950)).ToArray();

        // Act
        var ownership = StackProcesses.ResolveOwnership(listeners, processes, Worktree, Protected());

        // Assert
        ownership.Owned.Select(process => process.ProcessId).Should().NotContain(950);
        ownership.Foreign.Should().ContainSingle().Which.ProcessId.Should().Be(950);
    }

    [Fact]
    public void ResolveOwnership_WhenTheCallerItselfHoldsAnAllocatedPort_ShouldNotOwnTheCallerOrItsAncestors()
    {
        // Arrange: a stop that killed its own process tree would never finish.
        var processes = RunningStack
            .Append(new ProcessSnapshot(700, 1, $"bash {Worktree}/scripts/session.sh"))
            .Append(new ProcessSnapshot(701, 700, $"{Worktree}/developer-cli/bin/pp stop"))
            .ToArray();
        var listeners = RunningStackListeners.Append(Listener(9016, 701)).ToArray();

        // Act
        var ownership = StackProcesses.ResolveOwnership(listeners, processes, Worktree, StackProcesses.SelfAndAncestors(701, processes));

        // Assert
        ownership.Owned.Select(process => process.ProcessId).Should().NotContain(701).And.NotContain(700);
    }

    [Fact]
    public void ResolveOwnership_WhenOnlyContainerPortsAreHeld_ShouldOwnNothing()
    {
        // Act
        var ownership = StackProcesses.ResolveOwnership([Listener(9004, 500)], RunningStack, Worktree, Protected());

        // Assert
        ownership.Owned.Should().BeEmpty();
        ownership.Foreign.Should().BeEmpty();
        ownership.ContainerPorts.Should().ContainSingle();
    }

    [Fact]
    public void WaitUntilReleased_WhenEveryPortIsFreeOnTheFirstPoll_ShouldReportReleasedWithoutSleeping()
    {
        // Arrange
        var sleeps = new List<TimeSpan>();

        // Act
        var result = StackProcesses.WaitUntilReleased(() => [], Timeout, PollInterval, () => TimeSpan.Zero, sleeps.Add);

        // Assert
        result.Released.Should().BeTrue();
        result.StillHeld.Should().BeEmpty();
        sleeps.Should().BeEmpty();
    }

    [Fact]
    public void WaitUntilReleased_WhenThePortsAreFreedAfterTwoPolls_ShouldReportReleased()
    {
        // Arrange
        var polls = new Queue<PortListener[]>([[Listener(9009, 102)], [Listener(9009, 102)], []]);
        var elapsed = TimeSpan.Zero;

        // Act
        var result = StackProcesses.WaitUntilReleased(polls.Dequeue, Timeout, PollInterval, () => elapsed, interval => elapsed += interval);

        // Assert
        result.Released.Should().BeTrue();
        result.Elapsed.Should().Be(PollInterval * 2);
    }

    [Fact]
    public void WaitUntilReleased_WhenAPortStaysHeld_ShouldTimeOutAndNameTheHolder()
    {
        // Arrange
        var elapsed = TimeSpan.Zero;

        // Act
        var result = StackProcesses.WaitUntilReleased(() => [Listener(9009, 102)], Timeout, PollInterval, () => elapsed, interval => elapsed += interval);

        // Assert
        result.Released.Should().BeFalse();
        result.StillHeld.Should().ContainSingle().Which.Port.Should().Be(9009);
        result.Elapsed.Should().Be(Timeout);
    }

    [Fact]
    public void WaitUntilReleased_WhenOnlyAContainerPortRemains_ShouldReportReleased()
    {
        // Arrange: containers are stopped after the processes, by name, so their ports are not the stop's to wait for.
        var containerListener = new PortListener(9004, 500, "docker-proxy -proto tcp -host-port 9004");

        // Act
        var result = StackProcesses.WaitUntilReleased(() => [containerListener], Timeout, PollInterval, () => TimeSpan.Zero, _ => { });

        // Assert
        result.Released.Should().BeTrue();
    }

    [Fact]
    public void SelfAndAncestors_ShouldIncludeTheProcessAndEveryAncestor()
    {
        // Act
        var chain = StackProcesses.SelfAndAncestors(102, RunningStack);

        // Assert
        chain.Order().Should().Equal(0, 1, 100, 101, 102);
    }

    [Fact]
    public void ParseProcessSnapshot_WhenGivenProcessListingOutput_ShouldKeepTheCommandLineIntact()
    {
        // Arrange
        const string output = """
                                3825048       1 script -q -f -c dotnet run --project /repo/application/AppHost/AppHost.csproj
                                3825645 3825049 /repo/application/AppHost/bin/Debug/net10.0/AppHost
                              header line that is not a process
                             """;

        // Act
        var processes = StackProcesses.ParseProcessSnapshot(output);

        // Assert
        processes.Should().HaveCount(2);
        processes[0].Should().Be(new ProcessSnapshot(3825048, 1, "script -q -f -c dotnet run --project /repo/application/AppHost/AppHost.csproj"));
        processes[1].ParentProcessId.Should().Be(3825049);
    }

    [Theory]
    [InlineData("--monitor 102", new[] { 102 })]
    [InlineData("--monitor=102", new[] { 102 })]
    [InlineData("dcp run-controllers --monitor 1024 --child 7", new[] { 1024 })]
    [InlineData("dcp start-apiserver --kubeconfig /tmp/x", new int[0])]
    public void MonitoredProcessIds_ShouldMatchWholeTokensOnly(string commandLine, int[] expected)
    {
        // Act
        var monitored = StackProcesses.MonitoredProcessIds(commandLine);

        // Assert
        monitored.Should().Equal(expected);
    }

    [Theory]
    [InlineData("docker-proxy -proto tcp -host-port 9004", true)]
    [InlineData("/usr/bin/containerd-shim-runc-v2 -namespace moby", true)]
    [InlineData("/repo/application/AppHost/bin/Debug/net10.0/AppHost", false)]
    [InlineData("node /repo/application/node_modules/.bin/rsbuild dev", false)]
    public void IsContainerRuntime_ShouldRecogniseOnlyTheContainerRuntime(string commandLine, bool expected)
    {
        // Act
        var isContainerRuntime = StackProcesses.IsContainerRuntime(commandLine);

        // Assert
        isContainerRuntime.Should().Be(expected);
    }

    private static PortListener Listener(int port, int processId)
    {
        return new PortListener(port, processId, RunningStack.Concat(ExtraProcesses).First(process => process.ProcessId == processId).CommandLine);
    }

    private static IReadOnlySet<int> Protected()
    {
        return new HashSet<int>();
    }
}
