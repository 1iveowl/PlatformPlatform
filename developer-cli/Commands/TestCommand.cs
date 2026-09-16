using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Commands;

public class TestCommand : Command
{
    private const int MaxFailedTestsShown = 30;

    public TestCommand() : base("test", "Runs tests from a solution")
    {
        var backendOption = new Option<bool>("--backend", "-b") { Description = "Run only the tests of the application solution, without the Blazor build root" };
        var selfContainedSystemOption = new Option<string?>("<self-contained-system>", "--self-contained-system", "-s") { Description = "The name of the self-contained system to test (e.g., main, account, back-office)" };
        var gatewayOption = new Option<bool>("--gateway", "-g") { Description = "Scope tests to AppGateway.Tests" };
        var blazorOption = new Option<bool>("--blazor") { Description = "Run the tests of the Blazor build root (blazor/), which resolves its own SDK" };
        var cliOption = new Option<bool>("--cli", "-c") { Description = "Run the tests of the developer CLI (developer-cli/)" };
        var spikeOption = new Option<string?>("--spike") { Description = "Run only the tests of the spike solution in blazor/spike/<name>/, which resolves its own SDK" };
        var noBuildOption = new Option<bool>("--no-build") { Description = "Skip building and restoring the solution before running tests" };
        var quietOption = new Option<bool>("--quiet", "-q") { Description = "Print only failures and a one-line total (the default)" };
        var verboseOption = new Option<bool>("--verbose") { Description = "Print the full output of the underlying tools" };
        var filterOption = new Option<string?>("--filter") { Description = "Filter tests by name (dotnet test --filter)" };
        var excludeCategoryOption = new Option<string?>("--exclude-category") { Description = "Exclude tests by category (e.g., 'Noisy', 'RequiresDocker'). Defaults to 'Noisy'." };

        Options.Add(backendOption);
        Options.Add(selfContainedSystemOption);
        Options.Add(gatewayOption);
        Options.Add(blazorOption);
        Options.Add(cliOption);
        Options.Add(spikeOption);
        Options.Add(noBuildOption);
        Options.Add(quietOption);
        Options.Add(verboseOption);
        Options.Add(filterOption);
        Options.Add(excludeCategoryOption);

        SetAction(parseResult => Execute(
                parseResult.GetValue(backendOption),
                parseResult.GetValue(selfContainedSystemOption),
                parseResult.GetValue(gatewayOption),
                parseResult.GetValue(blazorOption),
                parseResult.GetValue(cliOption),
                parseResult.GetValue(spikeOption),
                parseResult.GetValue(noBuildOption),
                !parseResult.GetValue(verboseOption),
                parseResult.GetValue(filterOption),
                parseResult.GetValue(excludeCategoryOption)
            )
        );
    }

    // No target flag runs the application solution and the Blazor build root, as build does. The developer CLI tests run
    // only with --cli. An explicit backend selector (--backend, --self-contained-system or --gateway) picks one backend target.
    public static TestTarget[] SelectTestTargets(bool backend, string? selfContainedSystem, bool gateway, bool blazor, bool developerCli)
    {
        if (!backend && selfContainedSystem is null && !gateway && !blazor && !developerCli)
        {
            return [TestTarget.Application, TestTarget.Blazor];
        }

        var targets = new List<TestTarget>();
        if (gateway)
        {
            targets.Add(TestTarget.Gateway);
        }
        else if (selfContainedSystem is not null)
        {
            targets.Add(TestTarget.SelfContainedSystem);
        }
        else if (backend) targets.Add(TestTarget.Application);

        if (blazor) targets.Add(TestTarget.Blazor);
        if (developerCli) targets.Add(TestTarget.DeveloperCli);

        return targets.ToArray();
    }

    // A spike name is one lowercase kebab-case folder name, so it can never leave blazor/spike/
    public static bool IsValidSpikeName(string spikeName)
    {
        return Regex.IsMatch(spikeName, "^[a-z0-9]+(-[a-z0-9]+)*$");
    }

    public static string BuildBuildCommand(string targetName, bool quiet)
    {
        return quiet
            ? $"dotnet build {targetName}"
            : $"dotnet build {targetName} --verbosity quiet";
    }

    public static string BuildTestCommand(string targetName, string? filter, string? excludeCategory)
    {
        var filterArgument = BuildFilterArgument(filter, excludeCategory);
        return $"""dotnet test {targetName} --no-build --no-restore --logger "console;verbosity=normal"{filterArgument}""";
    }

    public static string BuildFilterArgument(string? userFilter, string? excludeCategory)
    {
        // By default, exclude "Noisy" category tests unless user explicitly specifies otherwise
        // Use empty string to disable default exclusion
        var categoryToExclude = excludeCategory ?? "Noisy";
        var categoryFilter = string.IsNullOrEmpty(categoryToExclude) ? "" : $"Category!={categoryToExclude}";

        if (userFilter is not null && categoryFilter != "")
        {
            // Combine user filter with category exclusion using AND (&)
            return $""" --filter "({userFilter})&{categoryFilter}" """;
        }

        if (userFilter is not null)
        {
            return $""" --filter "{userFilter}" """;
        }

        if (categoryFilter != "")
        {
            return $""" --filter "{categoryFilter}" """;
        }

        return "";
    }

    // Every selected target runs even when an earlier one failed; the first non-zero exit code is the command's exit code
    public static int CombineExitCodes(int[] exitCodes)
    {
        return exitCodes.FirstOrDefault(exitCode => exitCode != 0);
    }

    private static void Execute(bool backend, string? selfContainedSystem, bool gateway, bool blazor, bool developerCli, string? spike, bool noBuild, bool quiet, string? filter, string? excludeCategory)
    {
        Prerequisite.Ensure(Prerequisite.Dotnet);

        if (spike is not null)
        {
            ExecuteSpike(spike, backend || selfContainedSystem is not null || gateway || blazor || developerCli, noBuild, quiet, filter, excludeCategory);
            return;
        }

        if (gateway) AppGatewayHelper.EnsureNotCombinedWithSelfContainedSystem(selfContainedSystem);

        if (blazor && (gateway || selfContainedSystem is not null))
        {
            AnsiConsole.MarkupLine("[red]--blazor cannot be combined with --gateway or --self-contained-system.[/]");
            Environment.Exit(1);
        }

        try
        {
            var targets = SelectTestTargets(backend, selfContainedSystem, gateway, blazor, developerCli);
            var exitCodes = new List<int>();

            foreach (var target in targets)
            {
                var (targetName, workingDirectory) = ResolveTarget(target, selfContainedSystem);
                var summaryPrefix = targets.Length > 1 ? $"{targetName}: " : "";

                if (!noBuild)
                {
                    ProcessHelper.Run(BuildBuildCommand(targetName, quiet), workingDirectory, "Build", quiet);
                }

                var testCommand = BuildTestCommand(targetName, filter, excludeCategory);
                exitCodes.Add(quiet
                    ? RunTestsQuietly(testCommand, workingDirectory, summaryPrefix)
                    : RunTestsWithFilteredOutput(testCommand, workingDirectory, summaryPrefix)
                );
            }

            var exitCode = CombineExitCodes(exitCodes.ToArray());
            if (exitCode != 0) Environment.Exit(exitCode);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Tests failed: {ex.Message}");
            Environment.Exit(1);
        }
    }

    // Spike solutions are experiments outside every regular target; they run alone from their own folder so a global.json
    // there selects the SDK
    private static void ExecuteSpike(string spikeName, bool hasOtherTarget, bool noBuild, bool quiet, string? filter, string? excludeCategory)
    {
        if (hasOtherTarget)
        {
            AnsiConsole.MarkupLine("[red]--spike cannot be combined with another test target.[/]");
            Environment.Exit(1);
        }

        if (!IsValidSpikeName(spikeName))
        {
            AnsiConsole.MarkupLine($"[red]'{Markup.Escape(spikeName)}' is not a spike name. Use the lowercase kebab-case folder name under blazor/spike/.[/]");
            Environment.Exit(1);
        }

        var spikeFolder = Path.Combine(Configuration.BlazorFolder, "spike", spikeName);
        var solutionFiles = Directory.Exists(spikeFolder) ? Directory.GetFiles(spikeFolder, "*.slnx") : [];
        if (solutionFiles.Length != 1)
        {
            AnsiConsole.MarkupLine($"[red]blazor/spike/{spikeName}/ must contain exactly one .slnx file.[/]");
            Environment.Exit(1);
        }

        var solutionName = Path.GetFileName(solutionFiles[0]);
        if (!noBuild)
        {
            ProcessHelper.Run(BuildBuildCommand(solutionName, quiet), spikeFolder, "Build", quiet);
        }

        var testCommand = BuildTestCommand(solutionName, filter, excludeCategory);
        var exitCode = quiet ? RunTestsQuietly(testCommand, spikeFolder, "") : RunTestsWithFilteredOutput(testCommand, spikeFolder, "");
        if (exitCode != 0) Environment.Exit(exitCode);
    }

    private static (string TargetName, string? WorkingDirectory) ResolveTarget(TestTarget target, string? selfContainedSystem)
    {
        switch (target)
        {
            case TestTarget.Gateway:
                return (AppGatewayHelper.TestProjectRelativePath, Configuration.ApplicationFolder);
            case TestTarget.Blazor:
                // The Blazor root resolves the SDK from blazor/global.json, which applies only with blazor/ as the working directory
                return ("Blazor.slnx", Configuration.BlazorFolder);
            case TestTarget.DeveloperCli:
                return ("DeveloperCli.slnx", Configuration.CliFolder);
            case TestTarget.SelfContainedSystem:
            case TestTarget.Application:
                var solutionFile = SelfContainedSystemHelper.GetSolutionFile(target == TestTarget.SelfContainedSystem ? selfContainedSystem : null);
                return (solutionFile.Name, solutionFile.Directory?.FullName);
            default:
                throw new UnreachableException($"Unknown test target '{target}'.");
        }
    }

    private static int RunTestsWithFilteredOutput(string command, string? workingDirectory, string summaryPrefix)
    {
        if (Configuration.TraceEnabled)
        {
            AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(command)}[/]");
        }

        var stats = new TestStats();
        var stopwatch = Stopwatch.StartNew();

        // Parse command to get executable and arguments
        var parts = command.Split(' ', 2);
        var executable = parts[0];
        var arguments = parts.Length > 1 ? parts[1] : "";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDirectory != null)
        {
            processStartInfo.WorkingDirectory = workingDirectory;
        }

        using var process = Process.Start(processStartInfo)!;

        // Drain stderr asynchronously to prevent deadlock when the buffer fills
        var stderrTask = process.StandardError.ReadToEndAsync();

        // Stream stdout in real-time
        while (!process.StandardOutput.EndOfStream)
        {
            var line = process.StandardOutput.ReadLine();
            if (line == null) continue;

            if (ShouldFilterLine(line)) continue;

            var trimmedLine = line.TrimStart();

            // Count test results
            if (trimmedLine.StartsWith("Passed "))
            {
                stats.Passed++;
                Console.WriteLine(line);
            }
            else if (trimmedLine.StartsWith("Failed "))
            {
                stats.Failed++;
                stats.FailedTests.Add(ExtractTestName(line));
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(line)}[/]");
            }
            else if (trimmedLine.StartsWith("Skipped "))
            {
                stats.Skipped++;
                Console.WriteLine(line);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                // Print other non-filtered lines (e.g., error details, stack traces)
                Console.WriteLine(line);
            }
        }

        process.WaitForExit();
        stderrTask.GetAwaiter().GetResult();
        stopwatch.Stop();
        var duration = stopwatch.Elapsed.TotalSeconds;

        // Print our summary
        Console.WriteLine();
        if (stats.Failed > 0 || process.ExitCode != 0)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(summaryPrefix)}Test summary: total: {stats.Total}; failed: {stats.Failed}; succeeded: {stats.Passed}; skipped: {stats.Skipped}; duration: {duration:F1}s[/]");
        }
        else
        {
            Console.WriteLine($"{summaryPrefix}Test summary: total: {stats.Total}; failed: {stats.Failed}; succeeded: {stats.Passed}; skipped: {stats.Skipped}; duration: {duration:F1}s");
        }

        return process.ExitCode;
    }

    private static int RunTestsQuietly(string command, string? workingDirectory, string summaryPrefix)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = ProcessHelper.ExecuteQuietly(command, workingDirectory);
        stopwatch.Stop();

        var stats = ParseTestOutput(result.StdOut);
        var duration = stopwatch.Elapsed.TotalSeconds;

        // Print summary
        Console.WriteLine($"{summaryPrefix}Test summary: total: {stats.Total}; failed: {stats.Failed}; succeeded: {stats.Passed}; skipped: {stats.Skipped}; duration: {duration:F1}s");

        // If failures, show failed test names + link to log
        if (stats.Failed > 0)
        {
            Console.WriteLine("Failed tests:");
            foreach (var test in stats.FailedTests.Take(MaxFailedTestsShown))
            {
                Console.WriteLine($"  {test}");
            }

            if (stats.FailedTests.Count > MaxFailedTestsShown)
            {
                Console.WriteLine($"  ... and {stats.FailedTests.Count - MaxFailedTestsShown} more");
            }

            Console.WriteLine($"Full output: {result.TempFilePathWithSize}");
            return 1;
        }

        if (result.ExitCode != 0)
        {
            Console.WriteLine($"Full output: {result.TempFilePathWithSize}");
        }

        return result.ExitCode;
    }

    private static TestStats ParseTestOutput(string output)
    {
        var stats = new TestStats();

        foreach (var line in output.Split('\n'))
        {
            var trimmedLine = line.TrimStart();
            if (trimmedLine.StartsWith("Passed "))
            {
                stats.Passed++;
            }
            else if (trimmedLine.StartsWith("Failed "))
            {
                stats.Failed++;
                stats.FailedTests.Add(ExtractTestName(line));
            }
            else if (trimmedLine.StartsWith("Skipped "))
            {
                stats.Skipped++;
            }
        }

        return stats;
    }

    private static bool ShouldFilterLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;

        var trimmedLine = line.TrimStart();

        // Filter xUnit adapter noise
        if (trimmedLine.StartsWith("[xUnit.net")) return true;

        // Filter VSTest noise
        if (trimmedLine.StartsWith("Test run for ")) return true;
        if (trimmedLine.StartsWith("VSTest version ")) return true;
        if (trimmedLine.StartsWith("Microsoft (R) Test Execution")) return true;
        if (trimmedLine.StartsWith("Copyright (c) Microsoft")) return true;
        if (trimmedLine.StartsWith("Starting test execution")) return true;
        if (trimmedLine.StartsWith("A total of ")) return true;

        // Filter per-assembly summary lines (we generate our own)
        if (trimmedLine.StartsWith("Test Run Successful.")) return true;
        if (trimmedLine.StartsWith("Test Run Failed.")) return true;
        if (Regex.IsMatch(trimmedLine, "^Total tests:")) return true;
        if (Regex.IsMatch(trimmedLine, @"^\s*Passed\s*:")) return true;
        if (Regex.IsMatch(trimmedLine, @"^\s*Failed\s*:")) return true;
        if (Regex.IsMatch(trimmedLine, @"^\s*Skipped\s*:")) return true;
        if (Regex.IsMatch(trimmedLine, "^Total time:")) return true;

        return false;
    }

    private static string ExtractTestName(string line)
    {
        // Line format: "  Failed TestNamespace.TestClass.TestMethod [duration]"
        var trimmed = line.Trim();
        if (trimmed.StartsWith("Failed "))
        {
            var testPart = trimmed[7..]; // Remove "Failed "
            var bracketIndex = testPart.LastIndexOf('[');
            if (bracketIndex > 0)
            {
                return testPart[..(bracketIndex - 1)].Trim();
            }

            return testPart.Trim();
        }

        return trimmed;
    }

    private class TestStats
    {
        public int Passed { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }

        public int Total => Passed + Failed + Skipped;

        public List<string> FailedTests { get; } = [];
    }
}

public enum TestTarget
{
    Application,
    SelfContainedSystem,
    Gateway,
    Blazor,
    DeveloperCli
}
