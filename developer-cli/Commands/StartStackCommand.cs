using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using SharedKernel.Configuration;
using Spectre.Console;

namespace DeveloperCli.Commands;

/// <summary>
///     Command to start a fresh Aspire AppHost without the dashboard and wait until it serves requests. Continuous
///     integration uses it to run the same stack developers run; it never reuses a stack that is already running.
/// </summary>
public sealed class StartStackCommand : Command
{
    private const string AppHostname = "app.dev.localhost";
    private const string PostgresDataVolumeVariable = "APPHOST_POSTGRES_DATA_VOLUME";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // Every URL this command checks is on this machine, so connections go to loopback without asking the operating system to
    // resolve *.localhost names, which not every resolver does. The development certificate is not validated: this is a
    // readiness probe, and the browser tests pin the certificate themselves.
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            ConnectCallback = ConnectToLoopbackAsync
        }
    ) { Timeout = TimeSpan.FromSeconds(5) };

    public StartStackCommand() : base("start-stack", "Starts a fresh Aspire AppHost without the dashboard and waits until the gateway, the APIs and the workers are ready")
    {
        var withoutBlazorHostOption = new Option<bool>("--without-blazor-host") { Description = "Leave out the blazor-host resource so blazor-serve can serve the published host behind the gateway" };
        var freshDatabaseOption = new Option<bool>("--fresh-database") { Description = "Run Postgres on a disposable data volume of its own, recreated empty on every start, instead of this worktree's database" };
        var timeoutOption = new Option<int>("--timeout", "-t") { Description = "Seconds to wait for the stack to become ready. Defaults to 600", DefaultValueFactory = _ => 600 };

        Options.Add(withoutBlazorHostOption);
        Options.Add(freshDatabaseOption);
        Options.Add(timeoutOption);

        SetAction(parseResult => Execute(parseResult.GetValue(withoutBlazorHostOption), parseResult.GetValue(freshDatabaseOption), parseResult.GetValue(timeoutOption)));
    }

    private static void Execute(bool withoutBlazorHost, bool freshDatabase, int timeoutSeconds)
    {
        Prerequisite.Ensure(Prerequisite.Dotnet, Prerequisite.Node, Prerequisite.Docker);

        if (timeoutSeconds <= 0)
        {
            AnsiConsole.MarkupLine("[red]--timeout must be a positive number of seconds.[/]");
            Environment.Exit(1);
        }

        var ports = RunCommand.Ports;
        var busyPorts = ports.AllPorts.Where(StackProcesses.IsListening).ToArray();
        if (RunCommand.IsAspireRunning() || busyPorts.Length > 0)
        {
            var portList = busyPorts.Length > 0 ? $" (ports in use: {string.Join(", ", busyPorts)})" : "";
            AnsiConsole.MarkupLine($"[red]A stack is already running on base port {ports.BasePort}{portList}. start-stack never reuses a running stack; stop it with '{Configuration.AliasName} stop' first.[/]");
            Environment.Exit(1);
        }

        var environment = BuildAppHostEnvironment(withoutBlazorHost);
        if (freshDatabase)
        {
            var volumeName = FreshPostgresDataVolumeName(DockerVolumeNaming.ResolveVolumePrefix(Configuration.SourceCodeFolder), ports);
            RecreateFreshDatabaseVolume(volumeName);
            environment = [.. environment, (PostgresDataVolumeVariable, volumeName)];
        }

        var appHostProjectPath = Path.Combine(Configuration.ApplicationFolder, "AppHost", "AppHost.csproj");
        var logPath = RunCommand.AppHostLogPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        if (File.Exists(logPath)) File.Delete(logPath);

        var mode = withoutBlazorHost ? "without the blazor-host resource" : "with every resource";
        var database = freshDatabase ? "a fresh database" : "this worktree's database";
        AnsiConsole.MarkupLine($"[blue]Starting a fresh Aspire AppHost without the dashboard, {mode}, with {database}, on base port {ports.BasePort}...[/]");

        var startTime = Stopwatch.GetTimestamp();
        ProcessHelper.StartProcess(
            RunCommand.BuildDetachedCommand($"dotnet run --project {appHostProjectPath}", logPath),
            Configuration.ApplicationFolder,
            waitForExit: false,
            environmentVariables: environment
        );

        var probes = new StackProbes(ports, withoutBlazorHost);
        var result = StackReadiness.WaitUntilReady(probes.TakeSnapshot, TimeSpan.FromSeconds(timeoutSeconds), PollInterval, () => Stopwatch.GetElapsedTime(startTime), Thread.Sleep);

        if (result.Outcome == ReadinessOutcome.Ready)
        {
            AnsiConsole.MarkupLine($"[green]The stack is ready at https://{AppHostname}:{ports.AppGateway}/ after {result.Elapsed.Format()}[/]");
            foreach (var resource in result.LastSnapshot!.Resources)
            {
                AnsiConsole.MarkupLine($"[dim]{resource.Resource.EscapeMarkup()} ({resource.Target.EscapeMarkup()}): {resource.Detail.EscapeMarkup()}[/]");
            }

            AnsiConsole.MarkupLine($"[dim]Stop with:[/] [yellow]{Configuration.AliasName} stop[/]");
            AnsiConsole.MarkupLine($"[dim]Logs:[/] {logPath}");
            return;
        }

        foreach (var line in StackReadiness.DescribeFailure(result, timeoutSeconds))
        {
            AnsiConsole.MarkupLine($"[red]{line.EscapeMarkup()}[/]");
        }

        AnsiConsole.MarkupLine($"[red]See {logPath}[/]");

        // A half-started stack would hold the ports and the containers, so the next attempt could only fail on them
        StopCommand.StopWorktree(Configuration.SourceCodeFolder);
        Environment.Exit(result.ExitCode);
    }

    // Login providers and Stripe are turned off so the AppHost never waits for a parameter value, whatever the user secrets hold
    public static (string Name, string Value)[] BuildAppHostEnvironment(bool withoutBlazorHost)
    {
        return
        [
            ("APPHOST_DISABLE_DASHBOARD", "true"),
            ("APPHOST_EXCLUDE_BLAZOR_HOST", withoutBlazorHost ? "true" : "false"),
            ("Parameters__google-oauth-enabled", "false"),
            ("Parameters__entra-oauth-enabled", "false"),
            ("Parameters__mitid-oauth-enabled", "false"),
            ("Parameters__stripe-enabled", "false")
        ];
    }

    // Only start-stack uses this volume, never a developer's everyday database, so removing it before each start is safe and
    // guarantees the stack migrates an empty database rather than one an earlier run already migrated
    public static string FreshPostgresDataVolumeName(string volumePrefix, PortAllocation ports)
    {
        return $"{volumePrefix}{ports.VolumeNameInfix}-postgres-fresh-data";
    }

    private static void RecreateFreshDatabaseVolume(string volumeName)
    {
        ProcessHelper.ExecuteQuietly($"docker volume rm {volumeName}");
        if (ProcessHelper.ExecuteQuietly($"docker volume inspect {volumeName}").ExitCode == 0)
        {
            AnsiConsole.MarkupLine($"[red]The fresh database volume {volumeName} could not be removed; a container still uses it. Stop it with '{Configuration.AliasName} stop' first.[/]");
            Environment.Exit(1);
        }

        AnsiConsole.MarkupLine($"[blue]Postgres will start on the empty volume {volumeName}.[/]");
    }

    internal static HttpStatusCode? GetStatusCode(string url)
    {
        try
        {
            using var response = HttpClient.Send(new HttpRequestMessage(HttpMethod.Get, url));
            return response.StatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async ValueTask<Stream> ConnectToLoopbackAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync([IPAddress.Loopback, IPAddress.IPv6Loopback], context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

// The resources start-stack waits for, polled together into one snapshot
internal sealed class StackProbes(PortAllocation ports, bool withoutBlazorHost)
{
    private readonly ProcessWatch _accountWorkersProcess = new();
    private readonly ProcessWatch _mainWorkersProcess = new();

    public ReadinessSnapshot TakeSnapshot()
    {
        var gatewayUrl = $"https://app.dev.localhost:{ports.AppGateway}/";
        var accountApiReadyUrl = $"https://localhost:{ports.AccountApi}/internal-api/ready";
        var mainApiReadyUrl = $"https://localhost:{ports.MainApi}/internal-api/ready";

        // The AppHost starts each API only after its worker, so an API that answers means its worker was started
        var accountApiStatus = StartStackCommand.GetStatusCode(accountApiReadyUrl);
        var mainApiStatus = StartStackCommand.GetStatusCode(mainApiReadyUrl);

        List<ResourceObservation> resources =
        [
            ObserveWorker("account-workers", ports.AccountWorkers, _accountWorkersProcess, Path.Combine("account", "Workers", "Account.Workers.csproj"), accountApiStatus is not null),
            ObserveWorker("main-workers", ports.MainWorkers, _mainWorkersProcess, Path.Combine("main", "Workers", "Main.Workers.csproj"), mainApiStatus is not null),
            // The health endpoint answers 200 only when every registered health check passes
            StackReadiness.ObserveHttp("account-api", accountApiReadyUrl, HttpStatusCode.OK, accountApiStatus),
            StackReadiness.ObserveHttp("main-api", mainApiReadyUrl, HttpStatusCode.OK, mainApiStatus),
            // The gateway serves the main single page application at its root, so anything but 200 is a routing or backend failure
            StackReadiness.ObserveHttp("app-gateway", gatewayUrl, HttpStatusCode.OK, StartStackCommand.GetStatusCode(gatewayUrl))
        ];

        if (!withoutBlazorHost)
        {
            var blazorUrl = $"{gatewayUrl}blazor/";
            resources.Add(StackReadiness.ObserveHttp("blazor-host", blazorUrl, HttpStatusCode.OK, StartStackCommand.GetStatusCode(blazorUrl)));
        }

        return new ReadinessSnapshot(RunCommand.IsAspireRunning(), [.. resources]);
    }

    private static ResourceObservation ObserveWorker(string resource, int port, ProcessWatch processWatch, string projectRelativePath, bool dependentApiAnswering)
    {
        var listening = StackProcesses.IsListening(port);
        var processExited = processWatch.HasExited(IsProjectProcessRunning(projectRelativePath), dependentApiAnswering);
        return StackReadiness.ObserveWorker(resource, port, listening, processExited);
    }

    // Aspire runs each project with 'dotnet run --project <path>', so the project path identifies this worktree's process.
    // Windows has no pgrep; there a worker failure surfaces as a worker that never listens.
    private static bool IsProjectProcessRunning(string projectRelativePath)
    {
        if (Configuration.IsWindows) return true;

        var projectPath = Path.Combine(Configuration.ApplicationFolder, projectRelativePath);
        return !string.IsNullOrWhiteSpace(ProcessHelper.StartProcess($"pgrep -f {projectPath}", redirectOutput: true, exitOnError: false));
    }
}

public enum ResourceState
{
    Pending,
    Ready,
    Failed
}

public enum ReadinessOutcome
{
    Ready,
    AppHostExited,
    ResourceFailed,
    TimedOut
}

public sealed record ResourceObservation(string Resource, string Target, ResourceState State, string Detail);

// One poll of every resource. The stack counts as ready only when a single snapshot finds every resource ready, so an answer
// that was good in an earlier poll never stands in for the current one.
public sealed record ReadinessSnapshot(bool AppHostRunning, ResourceObservation[] Resources)
{
    public bool IsReady => AppHostRunning && Resources.All(resource => resource.State == ResourceState.Ready);
}

public sealed record ReadinessResult(ReadinessOutcome Outcome, ReadinessSnapshot? LastSnapshot, TimeSpan Elapsed)
{
    public int ExitCode => Outcome == ReadinessOutcome.Ready ? 0 : 1;
}

public static class StackReadiness
{
    public static ReadinessResult WaitUntilReady(Func<ReadinessSnapshot> takeSnapshot, TimeSpan timeout, TimeSpan pollInterval, Func<TimeSpan> elapsed, Action<TimeSpan> sleep)
    {
        ReadinessSnapshot? lastSnapshot = null;
        var appHostSeen = false;

        while (elapsed() < timeout)
        {
            sleep(pollInterval);
            lastSnapshot = takeSnapshot();

            appHostSeen |= lastSnapshot.AppHostRunning;
            if (appHostSeen && !lastSnapshot.AppHostRunning) return new ReadinessResult(ReadinessOutcome.AppHostExited, lastSnapshot, elapsed());
            if (lastSnapshot.Resources.Any(resource => resource.State == ResourceState.Failed)) return new ReadinessResult(ReadinessOutcome.ResourceFailed, lastSnapshot, elapsed());
            if (lastSnapshot.IsReady) return new ReadinessResult(ReadinessOutcome.Ready, lastSnapshot, elapsed());
        }

        return new ReadinessResult(ReadinessOutcome.TimedOut, lastSnapshot, elapsed());
    }

    // An HTTP resource is ready only on the status its endpoint documents as success. Any other answer, a 500 from the gateway
    // included, keeps it pending, and no answer at all is reported as such.
    public static ResourceObservation ObserveHttp(string resource, string url, HttpStatusCode expectedStatus, HttpStatusCode? actualStatus)
    {
        if (actualStatus is null) return new ResourceObservation(resource, url, ResourceState.Pending, "no response");

        var detail = $"HTTP {(int)actualStatus} {actualStatus}, expected {(int)expectedStatus} {expectedStatus}";
        return new ResourceObservation(resource, url, actualStatus == expectedStatus ? ResourceState.Ready : ResourceState.Pending, detail);
    }

    // A worker serves no endpoint; like its container app readiness probe in Azure, it is ready when its port accepts connections.
    // Program.cs opens that port only after migrations, data migrations and feature flag reconciliation have finished, so a
    // listening worker proves they did. A worker process that was seen and has gone has failed, whatever its port says.
    public static ResourceObservation ObserveWorker(string resource, int port, bool listening, bool processExited)
    {
        var target = $"tcp://localhost:{port}";
        if (processExited) return new ResourceObservation(resource, target, ResourceState.Failed, "the process exited");

        return listening
            ? new ResourceObservation(resource, target, ResourceState.Ready, "listening")
            : new ResourceObservation(resource, target, ResourceState.Pending, "not listening; startup, migrations or feature flag reconciliation not finished");
    }

    public static string[] DescribeFailure(ReadinessResult result, int timeoutSeconds)
    {
        var headline = result.Outcome switch
        {
            ReadinessOutcome.AppHostExited => "The AppHost exited before the stack was ready.",
            ReadinessOutcome.ResourceFailed => "A resource failed before the stack was ready.",
            ReadinessOutcome.TimedOut => $"The stack was not ready within {timeoutSeconds}s.",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Only a failed outcome has a failure description.")
        };

        if (result.LastSnapshot is null) return [headline, "No resource was polled."];

        var resourceLines = result.LastSnapshot.Resources
            .Where(resource => resource.State != ResourceState.Ready)
            .Select(resource => $"{resource.Resource} ({resource.Target}): {resource.State.ToString().ToLowerInvariant()}, {resource.Detail}");
        return [headline, .. resourceLines];
    }
}

// Tracks one worker process across polls. It has exited when it was running in an earlier poll and is not running now, or when
// it is not running while the API that the AppHost starts only after this worker is already answering: a worker that fails
// within one poll interval is never seen running, and without this it would surface only as a timeout.
public sealed class ProcessWatch
{
    private bool _seen;

    public bool HasExited(bool running, bool dependentApiAnswering)
    {
        var exited = !running && (_seen || dependentApiAnswering);
        _seen |= running;
        return exited;
    }
}
