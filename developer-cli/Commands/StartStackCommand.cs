using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Commands;

/// <summary>
///     Command to start a fresh Aspire AppHost without the dashboard and wait until it serves requests. Continuous integration
///     uses it to run the same stack developers run; it never reuses a stack that is already running.
/// </summary>
public sealed class StartStackCommand : Command
{
    private const string AppHostname = "app.dev.localhost";
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

    public StartStackCommand() : base("start-stack", "Starts a fresh Aspire AppHost without the dashboard and waits until the gateway and the account API are ready")
    {
        var withoutBlazorHostOption = new Option<bool>("--without-blazor-host") { Description = "Leave out the blazor-host resource so blazor-serve can serve the published host behind the gateway" };
        var timeoutOption = new Option<int>("--timeout", "-t") { Description = "Seconds to wait for the stack to become ready. Defaults to 600", DefaultValueFactory = _ => 600 };

        Options.Add(withoutBlazorHostOption);
        Options.Add(timeoutOption);

        SetAction(parseResult => Execute(parseResult.GetValue(withoutBlazorHostOption), parseResult.GetValue(timeoutOption)));
    }

    private static void Execute(bool withoutBlazorHost, int timeoutSeconds)
    {
        Prerequisite.Ensure(Prerequisite.Dotnet, Prerequisite.Node, Prerequisite.Docker);

        if (timeoutSeconds <= 0)
        {
            AnsiConsole.MarkupLine("[red]--timeout must be a positive number of seconds.[/]");
            Environment.Exit(1);
        }

        var ports = RunCommand.Ports;
        var busyPorts = ports.AllPorts.Where(IsListening).ToArray();
        if (RunCommand.IsAspireRunning() || busyPorts.Length > 0)
        {
            var portList = busyPorts.Length > 0 ? $" (ports in use: {string.Join(", ", busyPorts)})" : "";
            AnsiConsole.MarkupLine($"[red]A stack is already running on base port {ports.BasePort}{portList}. start-stack never reuses a running stack; stop it with '{Configuration.AliasName} stop' first.[/]");
            Environment.Exit(1);
        }

        var appHostProjectPath = Path.Combine(Configuration.ApplicationFolder, "AppHost", "AppHost.csproj");
        var logPath = RunCommand.AppHostLogPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        if (File.Exists(logPath)) File.Delete(logPath);

        var mode = withoutBlazorHost ? "without the blazor-host resource" : "with every resource";
        AnsiConsole.MarkupLine($"[blue]Starting a fresh Aspire AppHost without the dashboard, {mode}, on base port {ports.BasePort}...[/]");

        var startTime = Stopwatch.GetTimestamp();
        ProcessHelper.StartProcess(
            RunCommand.BuildDetachedCommand($"dotnet run --project {appHostProjectPath}", logPath),
            Configuration.ApplicationFolder,
            waitForExit: false,
            environmentVariables: BuildAppHostEnvironment(withoutBlazorHost)
        );

        var gatewayUrl = $"https://{AppHostname}:{ports.AppGateway}/";
        var accountApiReadyUrl = $"https://localhost:{ports.AccountApi}/internal-api/ready";
        var deadline = TimeSpan.FromSeconds(timeoutSeconds);
        var gatewayAnswered = false;
        var accountApiReady = false;
        var appHostSeen = false;

        while (Stopwatch.GetElapsedTime(startTime) < deadline)
        {
            Thread.Sleep(PollInterval);

            var appHostRunning = RunCommand.IsAspireRunning();
            appHostSeen |= appHostRunning;
            if (appHostSeen && !appHostRunning)
            {
                AnsiConsole.MarkupLine($"[red]The AppHost exited before the stack was ready. See {logPath}[/]");
                Environment.Exit(1);
            }

            gatewayAnswered = gatewayAnswered || GetStatusCode(gatewayUrl) is not null;
            accountApiReady = accountApiReady || GetStatusCode(accountApiReadyUrl) == HttpStatusCode.OK;
            if (gatewayAnswered && accountApiReady)
            {
                AnsiConsole.MarkupLine($"[green]The stack is ready at {gatewayUrl} after {Stopwatch.GetElapsedTime(startTime).Format()}[/]");
                AnsiConsole.MarkupLine($"[dim]Stop with:[/] [yellow]{Configuration.AliasName} stop[/]");
                AnsiConsole.MarkupLine($"[dim]Logs:[/] {logPath}");
                return;
            }
        }

        var pending = new List<string>();
        if (!gatewayAnswered) pending.Add($"the gateway did not answer at {gatewayUrl}");
        if (!accountApiReady) pending.Add($"the account API was not ready at {accountApiReadyUrl}");
        AnsiConsole.MarkupLine($"[red]The stack was not ready within {timeoutSeconds}s: {string.Join("; ", pending)}. See {logPath}[/]");

        // A half-started AppHost would hold the ports, so the next attempt could only fail on them
        RunCommand.StopAspire();
        Environment.Exit(1);
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

    private static HttpStatusCode? GetStatusCode(string url)
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

    private static bool IsListening(int port)
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
