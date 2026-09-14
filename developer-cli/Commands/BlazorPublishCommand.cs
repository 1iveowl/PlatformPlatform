using System.CommandLine;
using System.Diagnostics;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Commands;

public sealed class BlazorPublishCommand : Command
{
    // Git-ignored, so the artifact stays local to the worktree; blazor-serve and the harness scripts read it from here
    public static readonly string PublishFolder = Path.Combine(Configuration.SourceCodeFolder, ".workspace", "blazor-publish");

    public BlazorPublishCommand() : base("blazor-publish", "Publishes the Blazor host from the Blazor build root (blazor/) as a trimmed Release build")
    {
        var quietOption = new Option<bool>("--quiet", "-q") { Description = "Print only failures and a one-line result" };

        Options.Add(quietOption);

        SetAction(parseResult => Execute(parseResult.GetValue(quietOption)));
    }

    private static void Execute(bool quiet)
    {
        Prerequisite.Ensure(Prerequisite.Dotnet);

        try
        {
            var startTime = Stopwatch.GetTimestamp();
            if (Directory.Exists(PublishFolder)) Directory.Delete(PublishFolder, true);

            // Trimming (PublishTrimmed, TrimMode=partial) and publish-time Brotli are the SDK defaults for a Release publish of
            // the WebAssembly client; the feature switches remove the metrics, activity source and hot reload code paths
            var command = $"dotnet publish Blazor.Host/Blazor.Host.csproj -c Release -p:MetricsSupport=false -p:MetadataUpdaterSupport=false -p:WasmEnableHotReload=false -o \"{PublishFolder}\"";

            // The Blazor root resolves the SDK from blazor/global.json, which applies only with blazor/ as the working directory
            ProcessHelper.Run(command, Configuration.BlazorFolder, "Publish", quiet);

            AnsiConsole.MarkupLine($"[green]Published to {PublishFolder} in {Stopwatch.GetElapsedTime(startTime).Format()}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Publish failed: {Markup.Escape(ex.Message)}[/]");
            Environment.Exit(1);
        }
    }
}
