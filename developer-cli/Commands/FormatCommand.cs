using System.CommandLine;
using System.Diagnostics;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Commands;

public class FormatCommand : Command
{
    public FormatCommand() : base("format", "Formats code to match code styling rules")
    {
        var backendOption = new Option<bool>("--backend", "-b") { Description = "Format backend code" };
        var frontendOption = new Option<bool>("--frontend", "-f") { Description = "Format frontend code" };
        var cliOption = new Option<bool>("--cli", "-c") { Description = "Format developer-cli code" };
        var blazorOption = new Option<bool>("--blazor") { Description = "Format the Blazor build root (blazor/), which resolves its own SDK" };
        var selfContainedSystemOption = new Option<string?>("<self-contained-system>", "--self-contained-system", "-s") { Description = "The name of the self-contained system to format (e.g., main, account, back-office)" };
        var gatewayOption = new Option<bool>("--gateway", "-g") { Description = "Scope backend formatting to AppGateway and AppGateway.Tests" };
        var noBuildOption = new Option<bool>("--no-build") { Description = "Skip building and restoring before formatting" };
        var allFilesOption = new Option<bool>("--all-files") { Description = "Format every file in the solution. Default is to format only .cs files changed against origin/main." };
        var verifyBuildOption = new Option<bool>("--verify-build") { Description = "Build the Blazor build root after formatting it and fail when the formatted tree no longer builds" };
        var quietOption = new Option<bool>("--quiet", "-q") { Description = "Print only failures and a one-line total (the default)" };
        var verboseOption = new Option<bool>("--verbose") { Description = "Print the full output of the underlying tools" };

        Options.Add(backendOption);
        Options.Add(frontendOption);
        Options.Add(cliOption);
        Options.Add(blazorOption);
        Options.Add(selfContainedSystemOption);
        Options.Add(gatewayOption);
        Options.Add(noBuildOption);
        Options.Add(allFilesOption);
        Options.Add(verifyBuildOption);
        Options.Add(quietOption);
        Options.Add(verboseOption);

        SetAction(parseResult => Execute(
                parseResult.GetValue(backendOption),
                parseResult.GetValue(frontendOption),
                parseResult.GetValue(cliOption),
                parseResult.GetValue(blazorOption),
                parseResult.GetValue(selfContainedSystemOption),
                parseResult.GetValue(gatewayOption),
                parseResult.GetValue(noBuildOption),
                parseResult.GetValue(allFilesOption),
                parseResult.GetValue(verifyBuildOption),
                !parseResult.GetValue(verboseOption)
            )
        );
    }

    private static void Execute(bool backend, bool frontend, bool developerCli, bool blazor, string? selfContainedSystem, bool gateway, bool noBuild, bool allFiles, bool verifyBuild, bool quiet)
    {
        if (gateway) AppGatewayHelper.EnsureNotCombinedWithSelfContainedSystem(selfContainedSystem);

        var noFlags = !backend && !frontend && !developerCli && !blazor;
        var formatBackend = backend || noFlags;
        var formatFrontend = frontend || noFlags;
        var formatDeveloperCli = developerCli || noFlags;
        var formatBlazor = blazor || noFlags;

        if (verifyBuild && !formatBlazor)
        {
            AnsiConsole.MarkupLine("[red]--verify-build applies to the Blazor build root. Add --blazor.[/]");
            Environment.Exit(1);
        }

        try
        {
            // A plain format run must not let a later --verify-build run skip its build
            var cacheKey = verifyBuild ? "format-verify-build" : "format";
            if (SourceStateCache.IsUpToDate(cacheKey))
            {
                if (quiet)
                {
                    Console.WriteLine("No changes since last format run, skipping.");
                }
                else
                {
                    AnsiConsole.MarkupLine("[green]No changes since last format run, skipping.[/]");
                }

                return;
            }

            var initialUncommittedFiles = quiet ? null : GitHelper.GetChangedFiles();
            if (!quiet && initialUncommittedFiles!.Count > 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warning: You have unstaged changes in your working directory.[/]");
            }

            var startTime = Stopwatch.GetTimestamp();
            var backendTime = TimeSpan.Zero;
            var frontendTime = TimeSpan.Zero;
            var developerCliTime = TimeSpan.Zero;
            var blazorTime = TimeSpan.Zero;

            if (formatBackend)
            {
                Prerequisite.Ensure(Prerequisite.Dotnet);
                RunBackendFormat(selfContainedSystem, gateway, noBuild, allFiles, quiet);
                backendTime = Stopwatch.GetElapsedTime(startTime);
            }

            if (formatFrontend)
            {
                Prerequisite.Ensure(Prerequisite.Node);
                RunFrontendFormat(quiet);
                frontendTime = Stopwatch.GetElapsedTime(startTime) - backendTime;
            }

            if (formatDeveloperCli)
            {
                Prerequisite.Ensure(Prerequisite.Dotnet);
                var developerCliSolutionFile = new FileInfo(Path.Combine(Configuration.CliFolder, "DeveloperCli.slnx"));
                var changedFiles = allFiles ? null : GitHelper.GetChangedCsFilesInDirectory(developerCliSolutionFile.Directory!.FullName);
                RunSolutionFormat(developerCliSolutionFile, "developer-cli", noBuild, changedFiles, quiet);
                developerCliTime = Stopwatch.GetElapsedTime(startTime) - backendTime - frontendTime;
            }

            if (formatBlazor)
            {
                Prerequisite.Ensure(Prerequisite.Dotnet);
                RunBlazorFormat(noBuild, allFiles, verifyBuild, quiet);
                blazorTime = Stopwatch.GetElapsedTime(startTime) - backendTime - frontendTime - developerCliTime;
            }

            SourceStateCache.Save(cacheKey);

            if (quiet)
            {
                Console.WriteLine($"Code formatted successfully in {Stopwatch.GetElapsedTime(startTime).Format()}.");
            }
            else
            {
                var uncommittedFilesAfterFormat = GitHelper.GetChangedFiles();
                var modifiedFiles = uncommittedFilesAfterFormat
                    .Where(kvp => !initialUncommittedFiles!.TryGetValue(kvp.Key, out var hash) || hash != kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToArray();

                if (modifiedFiles.Length > 0)
                {
                    AnsiConsole.MarkupLine("[yellow]Warning: Code format modified the following files:[/]");
                    AnsiConsole.MarkupLine($"[blue]{string.Join(Environment.NewLine, modifiedFiles)}[/]");
                }

                AnsiConsole.MarkupLine($"[green]Code format completed in {Stopwatch.GetElapsedTime(startTime).Format()}[/]");

                var multipleTargets = (formatBackend ? 1 : 0) + (formatFrontend ? 1 : 0) + (formatDeveloperCli ? 1 : 0) + (formatBlazor ? 1 : 0) > 1;
                if (multipleTargets)
                {
                    var timingLines = new List<string>();
                    if (formatBackend) timingLines.Add($"Backend:       [green]{backendTime.Format()}[/]");
                    if (formatFrontend) timingLines.Add($"Frontend:      [green]{frontendTime.Format()}[/]");
                    if (formatDeveloperCli) timingLines.Add($"Developer CLI: [green]{developerCliTime.Format()}[/]");
                    if (formatBlazor) timingLines.Add($"Blazor:        [green]{blazorTime.Format()}[/]");
                    AnsiConsole.MarkupLine(string.Join(Environment.NewLine, timingLines));
                }
            }
        }
        catch (Exception ex)
        {
            if (quiet)
            {
                Console.WriteLine($"Format failed: {ex.Message}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error during code format: {ex.Message}[/]");
            }

            Environment.Exit(1);
        }
    }

    private static void RunBackendFormat(string? selfContainedSystem, bool gateway, bool noBuild, bool allFiles, bool quiet)
    {
        var solutionFile = SelfContainedSystemHelper.GetSolutionFile(gateway ? null : selfContainedSystem);

        if (!quiet) AnsiConsole.MarkupLine("[blue]Running backend code format...[/]");

        var includeArgument = string.Empty;
        if (gateway)
        {
            if (allFiles)
            {
                includeArgument = $""" --include="{AppGatewayHelper.IncludeGlob}" """.TrimEnd();
            }
            else
            {
                var changedCsFiles = AppGatewayHelper.FilterToAppGatewayFiles(GitHelper.GetChangedCsFilesInDirectory(solutionFile.Directory!.FullName));
                if (changedCsFiles.Length == 0)
                {
                    if (!quiet) AnsiConsole.MarkupLine("[green]No changed AppGateway C# files found, skipping backend format.[/]");
                    return;
                }

                includeArgument = $""" --include="{string.Join(";", changedCsFiles)}" """.TrimEnd();
                if (!quiet) AnsiConsole.MarkupLine($"[blue]Formatting {changedCsFiles.Length} changed AppGateway file(s)...[/]");
            }
        }
        else if (!allFiles)
        {
            var changedCsFiles = GitHelper.GetChangedCsFilesInDirectory(solutionFile.Directory!.FullName);
            if (changedCsFiles.Length == 0)
            {
                if (!quiet) AnsiConsole.MarkupLine("[green]No changed C# files found, skipping backend format.[/]");
                return;
            }

            includeArgument = $""" --include="{string.Join(";", changedCsFiles)}" """.TrimEnd();
            if (!quiet) AnsiConsole.MarkupLine($"[blue]Formatting {changedCsFiles.Length} changed file(s)...[/]");
        }

        if (!noBuild)
        {
            ProcessHelper.Run("dotnet tool restore", solutionFile.Directory!.FullName, "Tool restore", quiet);
        }

        ProcessHelper.Run(
            $"""dotnet jb cleanupcode {solutionFile.FullName} --profile=".NET only" --no-build{includeArgument}""",
            solutionFile.Directory!.FullName,
            "Format",
            quiet
        );
    }

    private static void RunFrontendFormat(bool quiet)
    {
        if (!quiet) AnsiConsole.MarkupLine("[blue]Running frontend code format...[/]");
        ProcessHelper.Run("npm run format", Configuration.ApplicationFolder, "Frontend format", quiet);
    }

    // The cleanup profile in blazor/Blazor.slnx.DotSettings turns reference shortening off. With it on, cleanup shortened a
    // fully qualified FluentUI enum in QuickUsersGrid.razor by adding @using Microsoft.FluentUI.AspNetCore.Components,
    // which made the TemplateColumn tag ambiguous between QuickGrid and FluentUI (RZ9985) and broke the build.
    // The cleanup runs with its own cache, emptied first, for the reason given in LintCommand.RunBlazorLinting.
    // Without --all-files the Blazor scope also formats untracked .cs and .razor files, so a new file is formatted before it is committed.
    private static void RunBlazorFormat(bool noBuild, bool allFiles, bool verifyBuild, bool quiet)
    {
        var solutionFile = new FileInfo(Path.Combine(Configuration.BlazorFolder, "Blazor.slnx"));
        var cachesHome = Path.Combine(Configuration.WorkspaceFolder, "developer-cli", "jetbrains-caches", "blazor-format");
        if (Directory.Exists(cachesHome)) Directory.Delete(cachesHome, true);

        var changedFiles = allFiles ? null : GitHelper.GetChangedAndUntrackedSourceFilesInDirectory(solutionFile.Directory!.FullName);
        RunSolutionFormat(solutionFile, "Blazor", noBuild, changedFiles, quiet, $" --caches-home={cachesHome}");

        if (!verifyBuild) return;

        if (!quiet) AnsiConsole.MarkupLine("[blue]Building the formatted Blazor build root...[/]");
        ProcessHelper.Run($"dotnet build {solutionFile.Name}", solutionFile.Directory!.FullName, "Build after format", quiet);
    }

    // Runs from the solution's own folder so the SDK in that folder's global.json is resolved. Null changedFiles formats every file.
    private static void RunSolutionFormat(FileInfo solutionFile, string displayName, bool noBuild, string[]? changedFiles, bool quiet, string cleanupArguments = "")
    {
        if (!quiet) AnsiConsole.MarkupLine($"[blue]Running {displayName} code format...[/]");

        var includeArgument = string.Empty;
        if (changedFiles is not null)
        {
            if (changedFiles.Length == 0)
            {
                if (!quiet) AnsiConsole.MarkupLine($"[green]No changed C# files found, skipping {displayName} format.[/]");
                return;
            }

            includeArgument = $""" --include="{string.Join(";", changedFiles)}" """.TrimEnd();
            if (!quiet) AnsiConsole.MarkupLine($"[blue]Formatting {changedFiles.Length} changed file(s)...[/]");
        }

        if (!noBuild)
        {
            ProcessHelper.Run("dotnet tool restore", solutionFile.Directory!.FullName, "Tool restore", quiet);
        }

        ProcessHelper.Run(
            $"""dotnet jb cleanupcode {solutionFile.FullName} --profile=".NET only" --no-build{cleanupArguments}{includeArgument}""",
            solutionFile.Directory!.FullName,
            "Format",
            quiet
        );
    }
}
