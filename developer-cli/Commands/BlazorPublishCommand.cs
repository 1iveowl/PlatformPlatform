using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
        var folderOption = new Option<string?>("--folder", "-f")
            { Description = "Keeps this publish beside the default one as .workspace/blazor-publish-<name>, so a release rehearsal can serve two publishes in turn" };
        var versionOption = new Option<string?>("--version")
            { Description = "The assembly version of this publish, which the client compares with the server's; a rehearsal publishes an older client with it" };

        Options.Add(quietOption);
        Options.Add(folderOption);
        Options.Add(versionOption);

        SetAction(parseResult => Execute(parseResult.GetValue(quietOption), parseResult.GetValue(folderOption), parseResult.GetValue(versionOption)));
    }

    // The default publish, or a named one beside it. A name is restricted to letters, digits and dashes so it can never
    // reach outside the workspace folder.
    public static string ResolvePublishFolder(string? folder)
    {
        if (folder is null) return PublishFolder;

        if (!Regex.IsMatch(folder, "^[a-z0-9-]{1,32}$"))
        {
            AnsiConsole.MarkupLine($"[red]Invalid folder name '{Markup.Escape(folder)}'. Use lowercase letters, digits and dashes.[/]");
            Environment.Exit(1);
        }

        return $"{PublishFolder}-{folder}";
    }

    private static void Execute(bool quiet, string? folder, string? version)
    {
        Prerequisite.Ensure(Prerequisite.Dotnet);

        try
        {
            var startTime = Stopwatch.GetTimestamp();
            var publishFolder = ResolvePublishFolder(folder);
            if (Directory.Exists(publishFolder)) Directory.Delete(publishFolder, true);

            // Trimming (PublishTrimmed, TrimMode=partial) and publish-time Brotli are the SDK defaults for a Release publish of
            // the WebAssembly client; the feature switches remove the metrics, activity source and hot reload code paths
            var versionArgument = version is null ? "" : $" -p:Version={version}";
            var command =
                $"dotnet publish Blazor.Host/Blazor.Host.csproj -c Release -p:MetricsSupport=false -p:MetadataUpdaterSupport=false -p:WasmEnableHotReload=false{versionArgument} -o \"{publishFolder}\"";

            // The Blazor root resolves the SDK from blazor/global.json, which applies only with blazor/ as the working directory
            ProcessHelper.Run(command, Configuration.BlazorFolder, "Publish", quiet);

            AnsiConsole.MarkupLine($"[green]Published to {publishFolder} in {Stopwatch.GetElapsedTime(startTime).Format()}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Publish failed: {Markup.Escape(ex.Message)}[/]");
            Environment.Exit(1);
        }
    }
}
