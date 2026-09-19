using System.CommandLine;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using SharedKernel.Configuration;
using Spectre.Console;

namespace DeveloperCli.Commands;

public sealed class BlazorServeCommand : Command
{
    private const string AppHostname = "app.dev.localhost";
    private const string PathBase = "/blazor";

    public BlazorServeCommand() : base("blazor-serve", "Runs the published Blazor host in Production on the Blazor host port, behind the running gateway")
    {
        SetAction(_ => Execute());
    }

    private static void Execute()
    {
        Prerequisite.Ensure(Prerequisite.Dotnet);

        var hostAssembly = Path.Combine(BlazorPublishCommand.PublishFolder, "Blazor.Host.dll");
        if (!File.Exists(hostAssembly))
        {
            AnsiConsole.MarkupLine($"[red]No published host at {hostAssembly}. Run blazor-publish first.[/]");
            Environment.Exit(1);
        }

        var ports = PortAllocation.LoadFrom(Configuration.SourceCodeFolder);
        if (StackProcesses.IsListening(ports.BlazorHost))
        {
            AnsiConsole.MarkupLine($"[red]Port {ports.BlazorHost} is in use; the gateway routes /blazor to this port. Start the stack with 'start-stack --without-blazor-host' instead of the full stack.[/]");
            Environment.Exit(1);
        }

        if (!StackProcesses.IsListening(ports.AppGateway) || !StackProcesses.IsListening(ports.AccountApi))
        {
            AnsiConsole.MarkupLine("[red]The gateway or the account API is not running. Start the stack with 'start-stack --without-blazor-host' first.[/]");
            Environment.Exit(1);
        }

        var gatewayUrl = $"https://{AppHostname}:{ports.AppGateway}";
        AnsiConsole.MarkupLine($"[blue]Serving the published host in Production at {gatewayUrl}{PathBase}/ (Ctrl+C to stop)[/]");

        // The same variables the AppHost sets on the blazor-host resource, except that the PUBLIC_*_ENABLED feature
        // variables are left unset. The token signing key comes from the user secrets store the AppHost writes on start.
        ProcessHelper.StartProcess(
            "dotnet Blazor.Host.dll",
            BlazorPublishCommand.PublishFolder,
            environmentVariables:
            [
                ("ASPNETCORE_ENVIRONMENT", "Production"),
                ("ASPNETCORE_URLS", $"https://localhost:{ports.BlazorHost}"),
                ("ACCOUNT_API_URL", $"https://localhost:{ports.AccountApi}"),
                ("PUBLIC_URL", gatewayUrl),
                ("CDN_URL", gatewayUrl + PathBase)
            ]
        );
    }
}
