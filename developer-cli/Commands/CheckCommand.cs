using System.CommandLine;
using System.Diagnostics;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Commands;

public class CheckCommand : Command
{
    public CheckCommand() : base("check", "Run build, test, format, and lint to verify everything before push")
    {
        var backendOption = new Option<bool>("--backend", "-b") { Description = "Check backend code only" };
        var frontendOption = new Option<bool>("--frontend", "-f") { Description = "Check frontend code only" };
        var cliOption = new Option<bool>("--cli", "-c") { Description = "Check developer-cli code only" };
        var blazorOption = new Option<bool>("--blazor") { Description = "Check the Blazor build root (blazor/) only" };
        var selfContainedSystemOption = new Option<string?>("<self-contained-system>", "--self-contained-system", "-s") { Description = "The name of the self-contained system to check (e.g., main, account, back-office)" };

        Options.Add(backendOption);
        Options.Add(frontendOption);
        Options.Add(cliOption);
        Options.Add(blazorOption);
        Options.Add(selfContainedSystemOption);

        SetAction(parseResult => Execute(
                parseResult.GetValue(backendOption),
                parseResult.GetValue(frontendOption),
                parseResult.GetValue(cliOption),
                parseResult.GetValue(blazorOption),
                parseResult.GetValue(selfContainedSystemOption)
            )
        );
    }

    // The developer CLI commands each step runs from developer-cli/. No target flag and no self-contained system checks backend,
    // frontend and the Blazor build root; its test step passes no target, so test runs the application solution and the Blazor
    // build root. A self-contained system scope without target flags keeps backend and frontend only.
    public static CheckStep[] BuildCheckSteps(bool backend, bool frontend, bool developerCli, bool blazor, string? selfContainedSystem)
    {
        var noFlags = !backend && !frontend && !developerCli && !blazor;

        var scsArgument = selfContainedSystem is not null ? $" --self-contained-system {selfContainedSystem}" : "";
        var backendFlag = backend || noFlags ? " --backend" : "";
        var frontendFlag = frontend || noFlags ? " --frontend" : "";
        var blazorFlag = blazor || (noFlags && selfContainedSystem is null) ? " --blazor" : "";
        var cliFlag = developerCli ? " --cli" : "";
        var targetFlags = $"{backendFlag}{frontendFlag}{blazorFlag}{cliFlag}";

        var steps = new List<CheckStep> { new("Build", $"dotnet run build{targetFlags}{scsArgument}") };

        if (noFlags)
        {
            steps.Add(new CheckStep("Test", $"dotnet run test{scsArgument}"));
        }
        else if (backend || blazor)
        {
            var testFlags = $"{(backend ? " --backend" : "")}{(blazor ? " --blazor" : "")}";
            steps.Add(new CheckStep("Test", $"dotnet run test{testFlags}{scsArgument}"));
        }

        steps.Add(new CheckStep("Format", $"dotnet run format{targetFlags}{scsArgument}"));
        steps.Add(new CheckStep("Lint", $"dotnet run lint{targetFlags}{scsArgument}"));

        return steps.ToArray();
    }

    private static void Execute(bool backend, bool frontend, bool developerCli, bool blazor, string? selfContainedSystem)
    {
        Prerequisite.Ensure(Prerequisite.Dotnet);
        Prerequisite.Ensure(Prerequisite.Node);

        var startTime = Stopwatch.GetTimestamp();

        var steps = BuildCheckSteps(backend, frontend, developerCli, blazor, selfContainedSystem);
        for (var index = 0; index < steps.Length; index++)
        {
            var step = steps[index];
            var separator = index == 0 ? "" : "\n";
            AnsiConsole.MarkupLine($"{separator}[blue]Step {index + 1}/{steps.Length}: {step.Name}[/]");
            ProcessHelper.Run(step.Command, Configuration.CliFolder, step.Name);
        }

        AnsiConsole.MarkupLine($"\n[green]All checks passed in {Stopwatch.GetElapsedTime(startTime).Format()}[/]");
    }
}

public sealed record CheckStep(string Name, string Command);
