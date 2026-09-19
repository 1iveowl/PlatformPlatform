using System.Net.Sockets;

namespace DeveloperCli.Utilities;

/// <summary>
///     Resolves which local processes belong to a worktree's Aspire stack, from the ports its allocation owns rather than
///     from a pattern over command lines. Two processes make a pattern unreliable: the AppHost's own executable is
///     "&lt;worktree&gt;/application/AppHost/bin/Debug/&lt;framework&gt;/AppHost", which nothing matching on "dotnet" finds, and
///     Aspire's orchestrator (dcp) is started outside the AppHost's process tree and so is not reached by walking it. Both
///     always hold a port from the allocation, which is what makes the ports the dependable signal.
/// </summary>
public static class StackProcesses
{
    // Processes that serve container ports on behalf of Docker. They are never part of a stack and are never killed;
    // containers are stopped separately and by name.
    private static readonly string[] ContainerRuntimeProcessNames =
        ["docker", "dockerd", "docker-proxy", "containerd", "containerd-shim", "containerd-shim-runc-v2"];

    // Only a launcher of this worktree's AppHost is followed upwards, never an operator's shell that happens to name the
    // repository, so the command line has to mention the AppHost as well as the worktree.
    private const string AppHostMarker = "AppHost";

    /// <summary>
    ///     Resolves the processes belonging to the stack of <paramref name="worktreePath" />. Pure, so the resolution is
    ///     tested against recorded snapshots rather than against live processes.
    /// </summary>
    public static StackOwnership ResolveOwnership(
        PortListener[] listeners,
        IReadOnlyCollection<ProcessSnapshot> processes,
        string worktreePath,
        IReadOnlySet<int> protectedProcessIds
    )
    {
        var byProcessId = processes
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.First());

        var childrenByParent = processes
            .GroupBy(process => process.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var owned = new Dictionary<int, StackProcess>();

        bool IsEligible(int processId)
        {
            if (protectedProcessIds.Contains(processId)) return false;

            return byProcessId.TryGetValue(processId, out var process) && !IsContainerRuntime(process.CommandLine);
        }

        // 1. Seeds: a process that holds one of the allocation's ports and names this worktree. This is the AppHost itself,
        //    the gateway, the APIs, the workers and the frontend development servers.
        foreach (var listener in listeners)
        {
            if (!IsEligible(listener.ProcessId)) continue;
            if (!listener.CommandLine.Contains(worktreePath, StringComparison.OrdinalIgnoreCase)) continue;

            owned[listener.ProcessId] = new StackProcess(listener.ProcessId, listener.CommandLine, $"holds port {listener.Port}");
        }

        // 2. The detached launcher above a seed ("script ... dotnet run --project <worktree>/application/AppHost/AppHost.csproj").
        //    A surviving launcher holds the log open and hides the stack from the next stop.
        foreach (var seedProcessId in owned.Keys.ToArray())
        {
            var parentProcessId = byProcessId[seedProcessId].ParentProcessId;
            while (byProcessId.TryGetValue(parentProcessId, out var parent))
            {
                if (!IsEligible(parentProcessId)) break;
                if (!parent.CommandLine.Contains(worktreePath, StringComparison.OrdinalIgnoreCase)) break;
                if (!parent.CommandLine.Contains(AppHostMarker, StringComparison.Ordinal)) break;

                owned.TryAdd(parentProcessId, new StackProcess(parentProcessId, parent.CommandLine, $"launcher of process {seedProcessId}"));
                parentProcessId = parent.ParentProcessId;
            }
        }

        // 3. Aspire's orchestrator monitors the AppHost by process id from outside its tree ("dcp start-apiserver --monitor
        //    <pid>"), and the dashboard and the port proxies run beneath it. Follow the monitor argument, then the descendants,
        //    until nothing new is found.
        var pending = new Queue<int>(owned.Keys);
        while (pending.Count > 0)
        {
            var processId = pending.Dequeue();

            foreach (var monitor in processes.Where(process => MonitoredProcessIds(process.CommandLine).Contains(processId)))
            {
                if (!IsEligible(monitor.ProcessId)) continue;
                if (!owned.TryAdd(monitor.ProcessId, new StackProcess(monitor.ProcessId, monitor.CommandLine, $"monitors process {processId}"))) continue;

                pending.Enqueue(monitor.ProcessId);
            }

            if (!childrenByParent.TryGetValue(processId, out var children)) continue;

            foreach (var child in children)
            {
                if (!IsEligible(child.ProcessId)) continue;
                if (!owned.TryAdd(child.ProcessId, new StackProcess(child.ProcessId, child.CommandLine, $"child of process {processId}"))) continue;

                pending.Enqueue(child.ProcessId);
            }
        }

        var foreign = listeners
            .Where(listener => !owned.ContainsKey(listener.ProcessId))
            .Where(listener => !IsContainerRuntime(listener.CommandLine))
            .ToArray();

        var containerPorts = listeners
            .Where(listener => IsContainerRuntime(listener.CommandLine))
            .ToArray();

        return new StackOwnership([.. owned.Values.OrderBy(process => process.ProcessId)], foreign, containerPorts);
    }

    /// <summary>
    ///     Waits until no port of the allocation has a listener any more. Container ports are excluded because the containers
    ///     are stopped after the processes, by name. Pure, with the clock and the sleep injected, like
    ///     <see cref="Commands.StackReadiness.WaitUntilReady" />.
    /// </summary>
    public static PortReleaseResult WaitUntilReleased(
        Func<PortListener[]> takeListeners,
        TimeSpan timeout,
        TimeSpan pollInterval,
        Func<TimeSpan> elapsed,
        Action<TimeSpan> sleep
    )
    {
        while (true)
        {
            var stillHeld = takeListeners()
                .Where(listener => !IsContainerRuntime(listener.CommandLine))
                .ToArray();

            if (stillHeld.Length == 0) return new PortReleaseResult(true, [], elapsed());
            if (elapsed() >= timeout) return new PortReleaseResult(false, stillHeld, elapsed());

            sleep(pollInterval);
        }
    }

    /// <summary>The process itself and every ancestor, which a stop must never kill or it terminates itself.</summary>
    public static IReadOnlySet<int> SelfAndAncestors(int processId, IReadOnlyCollection<ProcessSnapshot> processes)
    {
        var byProcessId = processes
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.First());

        var chain = new HashSet<int> { processId };
        var currentProcessId = processId;

        while (byProcessId.TryGetValue(currentProcessId, out var current) && chain.Add(current.ParentProcessId))
        {
            currentProcessId = current.ParentProcessId;
        }

        return chain;
    }

    /// <summary>
    ///     The process ids a command line says it monitors. Whole tokens only: a substring match would make "--monitor 102"
    ///     look like a monitor of process 1 and of process 10.
    /// </summary>
    public static IEnumerable<int> MonitoredProcessIds(string commandLine)
    {
        const string option = "--monitor";
        var tokens = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < tokens.Length; index++)
        {
            var value = tokens[index] == option && index + 1 < tokens.Length
                ? tokens[index + 1]
                : tokens[index].StartsWith($"{option}=", StringComparison.Ordinal)
                    ? tokens[index][(option.Length + 1)..]
                    : null;

            if (value is not null && int.TryParse(value, out var processId)) yield return processId;
        }
    }

    public static bool IsContainerRuntime(string commandLine)
    {
        var executable = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (executable is null) return false;

        var name = Path.GetFileName(executable);
        return ContainerRuntimeProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Parses "ps -eo pid=,ppid=,args=" output. Pure, so the resolution above is tested from recorded output.</summary>
    public static ProcessSnapshot[] ParseProcessSnapshot(string output)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 3 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => new ProcessSnapshot(int.Parse(parts[0]), int.Parse(parts[1]), parts[2]))
            .ToArray();
    }

    public static ProcessSnapshot[] TakeProcessSnapshot()
    {
        return ParseProcessSnapshot(ProcessHelper.StartProcess("ps -eo pid=,ppid=,args=", redirectOutput: true, exitOnError: false));
    }

    /// <summary>Every listener on the given ports, with the command line taken from the process snapshot.</summary>
    public static PortListener[] TakeListeners(int[] ports, IReadOnlyCollection<ProcessSnapshot> processes)
    {
        var commandLineByProcessId = processes
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.First().CommandLine);

        return ports
            .SelectMany(port => ListeningProcessIds(port).Select(processId => (Port: port, ProcessId: processId)))
            .Select(listener => new PortListener(
                    listener.Port,
                    listener.ProcessId,
                    commandLineByProcessId.GetValueOrDefault(listener.ProcessId, string.Empty)
                )
            )
            .ToArray();
    }

    private static int[] ListeningProcessIds(int port)
    {
        var output = ProcessHelper.StartProcess($"lsof -i :{port} -sTCP:LISTEN -t", redirectOutput: true, exitOnError: false);

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var processId) ? processId : 0)
            .Where(processId => processId > 0)
            .Distinct()
            .ToArray();
    }

    /// <summary>Whether a port accepts a connection. Needs no external tool, so it works wherever the CLI runs.</summary>
    public static bool IsListening(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("localhost", port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}

public sealed record ProcessSnapshot(int ProcessId, int ParentProcessId, string CommandLine);

public sealed record PortListener(int Port, int ProcessId, string CommandLine);

public sealed record StackProcess(int ProcessId, string CommandLine, string Reason);

/// <summary>
///     Owned processes are the stack's and are stopped. Foreign listeners are another program's and are only reported, so a
///     stop never kills what it does not own. Container ports are Docker's and are released when the containers stop.
/// </summary>
public sealed record StackOwnership(StackProcess[] Owned, PortListener[] Foreign, PortListener[] ContainerPorts);

public sealed record PortReleaseResult(bool Released, PortListener[] StillHeld, TimeSpan Elapsed);
