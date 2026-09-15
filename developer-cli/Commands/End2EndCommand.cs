using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Commands;

public partial class End2EndCommand : Command
{
    // npx cannot resolve Playwright from blazor/, which has no node_modules, so the Blazor run starts the CLI of the
    // @playwright/test package pinned in application/package.json directly
    private const string BlazorPlaywrightCli = "../application/node_modules/@playwright/test/cli.js";
    private static readonly string[] ValidBrowsers = ["chromium", "firefox", "webkit", "safari", "all"];

    // Get available self-contained systems
    private static readonly string[] AvailableSelfContainedSystems = SelfContainedSystemHelper.GetAvailableSelfContainedSystems();

    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            ConnectCallback = ConnectAsync
        }
    ) { Timeout = TimeSpan.FromSeconds(5) };

    public End2EndCommand() : base("e2e", "Run end-to-end tests using Playwright")
    {
        var searchTermsArgument = new Argument<string[]>("search-terms") { Description = "Search terms for test filtering (e.g., 'user management', '@smoke', 'smoke', 'comprehensive', 'user-management-flows.spec.ts')", DefaultValueFactory = _ => [] };
        var browserOption = new Option<string>("--browser", "-b") { Description = "Browser to use for tests (chromium, firefox, webkit, safari, all). Defaults to chromium", DefaultValueFactory = _ => "chromium" };
        var debugOption = new Option<bool>("--debug") { Description = "Start with Playwright Inspector for debugging (automatically enables headed mode)" };
        var debugTimingOption = new Option<bool>("--debug-timing") { Description = "Show step timing output with color coding during test execution" };
        var headedOption = new Option<bool>("--headed") { Description = "Show browser UI while running tests (automatically enables sequential execution)" };
        var includeSlowOption = new Option<bool>("--include-slow") { Description = "Include tests marked as @slow" };
        var lastFailedOption = new Option<bool>("--last-failed") { Description = "Only re-run the failures" };
        var onlyChangedOption = new Option<bool>("--only-changed") { Description = "Only run test files that have uncommitted changes" };
        var quietOption = new Option<bool>("--quiet") { Description = "Suppress all output including terminal output and automatic report opening" };
        var repeatEachOption = new Option<int?>("--repeat-each") { Description = "Number of times to repeat each test" };
        var deleteArtifactsOption = new Option<bool>("--delete-artifacts") { Description = "Delete all test artifacts and exit" };
        var retriesOption = new Option<int?>("--retries") { Description = "Maximum retry count for flaky tests, zero for no retries" };
        var selfContainedSystemOption = new Option<string?>("<self-contained-system>", "--self-contained-system", "-s") { Description = $"The name of the self-contained system to test ({string.Join(", ", AvailableSelfContainedSystems)}, etc.)" };
        var showReportOption = new Option<bool>("--show-report") { Description = "Always show HTML report after test run" };
        var slowMoOption = new Option<bool>("--slow-mo") { Description = "Run tests in slow motion (automatically enables headed mode)" };
        var smokeOption = new Option<bool>("--smoke") { Description = "Run only smoke tests" };
        var stopOnFirstFailureOption = new Option<bool>("--stop-on-first-failure", "-x") { Description = "Stop after the first failure" };
        var uiOption = new Option<bool>("--ui") { Description = "Run tests in interactive UI mode with time-travel debugging" };
        var workersOption = new Option<int?>("--workers", "-w") { Description = "Number of worker processes to use for running tests" };
        var configOption = new Option<bool>("--config") { Description = "Configure test performance profile for this machine (workers, timeouts)" };
        var noWaitForAspireOption = new Option<bool>("--no-wait-for-aspire") { Description = "Skip waiting for Aspire to start (by default, retries server check up to 30 seconds)" };
        var blazorOption = new Option<bool>("--blazor") { Description = "Run the end-to-end tests of the Blazor build root (blazor/tests/e2e) instead of the self-contained systems" };

        Arguments.Add(searchTermsArgument);
        Options.Add(browserOption);
        Options.Add(debugOption);
        Options.Add(debugTimingOption);
        Options.Add(headedOption);
        Options.Add(includeSlowOption);
        Options.Add(lastFailedOption);
        Options.Add(onlyChangedOption);
        Options.Add(quietOption);
        Options.Add(repeatEachOption);
        Options.Add(deleteArtifactsOption);
        Options.Add(retriesOption);
        Options.Add(selfContainedSystemOption);
        Options.Add(showReportOption);
        Options.Add(slowMoOption);
        Options.Add(smokeOption);
        Options.Add(stopOnFirstFailureOption);
        Options.Add(uiOption);
        Options.Add(workersOption);
        Options.Add(configOption);
        Options.Add(noWaitForAspireOption);
        Options.Add(blazorOption);

        // SetHandler only supports up to 8 parameters, so we use SetAction for this complex command
        SetAction(parseResult => Execute(
                parseResult.GetValue(searchTermsArgument)!,
                parseResult.GetValue(browserOption)!,
                parseResult.GetValue(debugOption),
                parseResult.GetValue(debugTimingOption),
                parseResult.GetValue(headedOption),
                parseResult.GetValue(includeSlowOption),
                parseResult.GetValue(lastFailedOption),
                parseResult.GetValue(onlyChangedOption),
                parseResult.GetValue(quietOption),
                parseResult.GetValue(repeatEachOption),
                parseResult.GetValue(deleteArtifactsOption),
                parseResult.GetValue(retriesOption),
                parseResult.GetValue(selfContainedSystemOption),
                parseResult.GetValue(showReportOption),
                parseResult.GetValue(slowMoOption),
                parseResult.GetValue(smokeOption),
                parseResult.GetValue(stopOnFirstFailureOption),
                parseResult.GetValue(uiOption),
                parseResult.GetValue(workersOption),
                parseResult.GetValue(configOption) || parseResult.GetValue(searchTermsArgument) is ["config"],
                parseResult.GetValue(noWaitForAspireOption),
                parseResult.GetValue(blazorOption)
            )
        );
    }

    private static string BaseUrl => Environment.GetEnvironmentVariable("PUBLIC_URL") ?? $"https://app.dev.localhost:{RunCommand.Ports.AppGateway}";

    private static string DefaultsFilePath => Path.Combine(Configuration.WorkspaceFolder, "developer-cli", "end-to-end-tests", "e2e-defaults.json");

    private static string BlazorTestsFolder => Path.Combine(Configuration.BlazorFolder, "tests");

    // Browsers and curl resolve every *.localhost name to loopback (RFC 6761), but the operating system resolver does not
    // have to: a container whose hosts file lists only localhost fails to resolve app.dev.localhost. The server check
    // therefore connects to loopback itself for those names, so it probes the same server the browsers reach.
    public static bool IsLoopbackHostName(string hostName)
    {
        var trimmedHostName = hostName.TrimEnd('.');
        return trimmedHostName.Equals("localhost", StringComparison.OrdinalIgnoreCase) || trimmedHostName.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ValidateBlazorOptions(bool blazor, string? selfContainedSystem)
    {
        return blazor && selfContainedSystem is not null ? "--blazor cannot be combined with --self-contained-system." : null;
    }

    // The Blazor host is served by the gateway under its /blazor/ path base, so the gateway root answering does not prove it is up
    public static string GetServerCheckUrl(string baseUrl, bool blazor)
    {
        return blazor ? $"{baseUrl.TrimEnd('/')}/blazor/" : baseUrl;
    }

    public static string BuildSelfContainedSystemPlaywrightCommand(string playwrightArgs, bool isWindows)
    {
        return $"{(isWindows ? "cmd.exe /C npx" : "npx")} playwright test --config=./tests/playwright.config.ts {playwrightArgs}";
    }

    public static string BuildBlazorPlaywrightArguments(string playwrightArgs)
    {
        return $"{BlazorPlaywrightCli} test --config=./tests/playwright.config.ts {playwrightArgs}";
    }

    public static string BuildBlazorPlaywrightCommand(string playwrightArgs)
    {
        return $"node {BuildBlazorPlaywrightArguments(playwrightArgs)}";
    }

    public static (string Name, string Value)[] BuildPlaywrightEnvironmentVariables(string baseUrl, bool slowMo, bool debugTiming, int? assertionTimeout)
    {
        var environmentVariables = new List<(string Name, string Value)> { ("PUBLIC_URL", baseUrl), ("PLAYWRIGHT_HTML_OPEN", "never") };
        if (slowMo) environmentVariables.Add(("PLAYWRIGHT_SLOW_MO", "500"));
        if (baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)) environmentVariables.Add(("PLAYWRIGHT_VIDEO_MODE", "on"));
        if (debugTiming) environmentVariables.Add(("PLAYWRIGHT_SHOW_DEBUG_TIMING", "true"));
        if (assertionTimeout is not null) environmentVariables.Add(("PLAYWRIGHT_EXPECT_TIMEOUT", (assertionTimeout.Value * 1000).ToString()));
        return environmentVariables.ToArray();
    }

    public static string BuildPlaywrightArgs(
        string[] testPatterns,
        string browser,
        bool debug,
        string? grep,
        bool showBrowser,
        bool includeSlow,
        bool lastFailed,
        bool onlyChanged,
        int? repeatEach,
        int? retries,
        bool runSequential,
        bool smoke,
        bool stopOnFirstFailure,
        bool ui,
        int? workers)
    {
        var args = new List<string>();

        // Handle browser project first as it affects test selection
        if (!browser.Equals("all", StringComparison.CurrentCultureIgnoreCase))
        {
            var playwrightBrowser = browser.ToLower() == "safari" ? "webkit" : browser.ToLower();
            args.Add($"--project={playwrightBrowser}");
        }

        // Handle test patterns - they should be relative to the tests/e2e directory
        if (testPatterns.Length > 0)
        {
            args.AddRange(testPatterns.Select(pattern =>
                    pattern.StartsWith("./") || pattern.StartsWith("tests/e2e/") ? pattern : $"./tests/e2e/{pattern}"
                )
            );
        }

        // Handle test filtering
        if (grep != null)
        {
            args.Add($"--grep=\"{grep}\"");
        }

        if (smoke) args.Add("--grep=\"@smoke\"");
        if (!includeSlow) args.Add("--grep-invert=\"@slow\"");

        // Handle test execution options
        if (ui) args.Add("--ui");
        if (debug) args.Add("--debug");
        if (showBrowser) args.Add("--headed");
        if (lastFailed) args.Add("--last-failed");
        if (onlyChanged) args.Add("--only-changed");
        if (repeatEach.HasValue) args.Add($"--repeat-each={repeatEach.Value}");
        if (retries.HasValue) args.Add($"--retries={retries.Value}");
        if (workers.HasValue)
        {
            args.Add($"--workers={workers.Value}");
        }
        else if (runSequential) args.Add("--workers=1");

        if (stopOnFirstFailure) args.Add("-x");

        return string.Join(" ", args);
    }

    private static void Execute(
        string[] searchTerms,
        string browser,
        bool debug,
        bool debugTiming,
        bool headed,
        bool includeSlow,
        bool lastFailed,
        bool onlyChanged,
        bool quiet,
        int? repeatEach,
        bool deleteArtifacts,
        int? retries,
        string? selfContainedSystem,
        bool showReport,
        bool slowMo,
        bool smoke,
        bool stopOnFirstFailure,
        bool ui,
        int? workers,
        bool configure,
        bool noWaitForAspire,
        bool blazor)
    {
        Prerequisite.Ensure(Prerequisite.Node);

        var blazorOptionError = ValidateBlazorOptions(blazor, selfContainedSystem);
        if (blazorOptionError is not null)
        {
            AnsiConsole.MarkupLine($"[red]{blazorOptionError}[/]");
            Environment.Exit(1);
        }

        if (configure)
        {
            ConfigurePerformanceProfile();
            Environment.Exit(0);
        }

        // Apply saved defaults if not explicitly provided
        if (!File.Exists(DefaultsFilePath) && !quiet)
        {
            AnsiConsole.MarkupLine($"[yellow]Tip: Run '{Configuration.AliasName} e2e config' to set worker count and timeouts for this machine.[/]");
        }

        workers ??= LoadDefault("workers");

        if (deleteArtifacts)
        {
            DeleteAllTestArtifacts(blazor);
            if (!quiet) AnsiConsole.MarkupLine("[yellow]Note: --delete-artifacts is a standalone operation and exits after cleaning artifacts.[/]");
            Environment.Exit(0);
        }

        if (blazor && !Directory.Exists(Path.Combine(BlazorTestsFolder, "e2e")))
        {
            AnsiConsole.MarkupLine("[red]No end-to-end tests found in blazor/tests/e2e.[/]");
            Environment.Exit(1);
        }

        if (!quiet) AnsiConsole.MarkupLine("[blue]Checking server availability...[/]");
        CheckWebsiteAccessibility(GetServerCheckUrl(BaseUrl, blazor), !noWaitForAspire, quiet);

        PlaywrightInstaller.EnsurePlaywrightBrowsers(quiet);

        // Convert search terms to test patterns and grep patterns
        var (testPatterns, searchGrep) = ProcessSearchTerms(searchTerms);

        if (blazor)
        {
            ExecuteBlazor(testPatterns, browser, debug, debugTiming, searchGrep, headed, includeSlow, lastFailed, onlyChanged, repeatEach, retries,
                showReport, slowMo, smoke, stopOnFirstFailure, ui, workers, quiet
            );
            return;
        }

        // Determine which self-contained systems to test based on the provided patterns or grep
        string[] selfContainedSystemsToTest;
        if (selfContainedSystem is not null)
        {
            if (!AvailableSelfContainedSystems.Contains(selfContainedSystem))
            {
                Console.WriteLine($"Invalid self-contained system '{selfContainedSystem}'. Available systems: {string.Join(", ", AvailableSelfContainedSystems)}");
                Environment.Exit(1);
            }

            selfContainedSystemsToTest = [selfContainedSystem];
        }
        else
        {
            selfContainedSystemsToTest = DetermineSystemsToTest(testPatterns, searchGrep, AvailableSelfContainedSystems);
        }

        // If debug or UI mode is enabled, we need a specific self-contained system
        if ((debug || ui) && selfContainedSystem is null)
        {
            if (selfContainedSystemsToTest.Length == 1)
            {
                selfContainedSystem = selfContainedSystemsToTest[0];
                selfContainedSystemsToTest = [selfContainedSystem];
            }
            else
            {
                selfContainedSystem = SelfContainedSystemHelper.PromptForSelfContainedSystem(
                    selfContainedSystemsToTest.Length > 0 ? selfContainedSystemsToTest : AvailableSelfContainedSystems
                );
                selfContainedSystemsToTest = [selfContainedSystem];
            }
        }

        // Validate browser option
        if (!ValidBrowsers.Contains(browser.ToLower()))
        {
            Console.WriteLine($"Invalid browser '{browser}'. Valid options are: {string.Join(", ", ValidBrowsers)}");
            Environment.Exit(1);
        }

        var stopwatch = Stopwatch.StartNew();
        var overallSuccess = true;
        var failedSelfContainedSystems = new List<string>();
        var showBrowser = headed || debug || slowMo;
        var useCombinedRun = selfContainedSystemsToTest.Length > 1 && !debug && !ui && !showBrowser;

        if (useCombinedRun)
        {
            overallSuccess = RunTestsCombined(selfContainedSystemsToTest, testPatterns, browser, debugTiming, searchGrep, includeSlow, lastFailed,
                onlyChanged, repeatEach, retries, showReport, smoke, stopOnFirstFailure, workers, quiet
            );
            if (!overallSuccess) failedSelfContainedSystems.AddRange(selfContainedSystemsToTest);
        }
        else
        {
            foreach (var currentSelfContainedSystem in selfContainedSystemsToTest)
            {
                var selfContainedSystemSuccess = RunTestsForSystem(currentSelfContainedSystem, testPatterns, browser, debug, debugTiming, searchGrep, headed, includeSlow, lastFailed,
                    onlyChanged, repeatEach, retries, showReport, slowMo, smoke, stopOnFirstFailure, ui, workers, quiet
                );

                if (!selfContainedSystemSuccess)
                {
                    overallSuccess = false;
                    failedSelfContainedSystems.Add(currentSelfContainedSystem);

                    if (stopOnFirstFailure) break;
                }
            }
        }

        stopwatch.Stop();

        if (quiet)
        {
            Console.WriteLine(overallSuccess
                ? $"All tests completed in {stopwatch.Elapsed.TotalSeconds:F1}s."
                : $"Some tests failed in {stopwatch.Elapsed.TotalSeconds:F1}s."
            );
        }
        else
        {
            AnsiConsole.MarkupLine(overallSuccess
                ? $"[green]All tests completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds[/]"
                : $"[red]Some tests failed in {stopwatch.Elapsed.TotalSeconds:F1} seconds[/]"
            );

            if (useCombinedRun)
            {
                if (showReport || !overallSuccess) OpenCombinedHtmlReport();
            }
            else if (showReport)
            {
                foreach (var currentSelfContainedSystem in selfContainedSystemsToTest)
                {
                    OpenHtmlReport(currentSelfContainedSystem);
                }
            }
            else if (!overallSuccess)
            {
                foreach (var currentSelfContainedSystem in failedSelfContainedSystems)
                {
                    OpenHtmlReport(currentSelfContainedSystem);
                }
            }
        }

        if (!overallSuccess) Environment.Exit(1);
    }

    private static void ExecuteBlazor(
        string[] testPatterns,
        string browser,
        bool debug,
        bool debugTiming,
        string? searchGrep,
        bool headed,
        bool includeSlow,
        bool lastFailed,
        bool onlyChanged,
        int? repeatEach,
        int? retries,
        bool showReport,
        bool slowMo,
        bool smoke,
        bool stopOnFirstFailure,
        bool ui,
        int? workers,
        bool quiet)
    {
        if (!ValidBrowsers.Contains(browser.ToLower()))
        {
            Console.WriteLine($"Invalid browser '{browser}'. Valid options are: {string.Join(", ", ValidBrowsers)}");
            Environment.Exit(1);
        }

        var stopwatch = Stopwatch.StartNew();
        var success = RunBlazorTests(testPatterns, browser, debug, debugTiming, searchGrep, headed, includeSlow, lastFailed, onlyChanged, repeatEach, retries,
            showReport, slowMo, smoke, stopOnFirstFailure, ui, workers, quiet
        );
        stopwatch.Stop();

        if (quiet)
        {
            Console.WriteLine(success
                ? $"All tests completed in {stopwatch.Elapsed.TotalSeconds:F1}s."
                : $"Some tests failed in {stopwatch.Elapsed.TotalSeconds:F1}s."
            );
        }
        else
        {
            AnsiConsole.MarkupLine(success
                ? $"[green]All tests completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds[/]"
                : $"[red]Some tests failed in {stopwatch.Elapsed.TotalSeconds:F1} seconds[/]"
            );

            if (showReport || !success) OpenBlazorHtmlReport();
        }

        if (!success) Environment.Exit(1);
    }

    private static bool RunBlazorTests(
        string[] testPatterns,
        string browser,
        bool debug,
        bool debugTiming,
        string? searchGrep,
        bool headed,
        bool includeSlow,
        bool lastFailed,
        bool onlyChanged,
        int? repeatEach,
        int? retries,
        bool showReport,
        bool slowMo,
        bool smoke,
        bool stopOnFirstFailure,
        bool ui,
        int? workers,
        bool quiet)
    {
        if (!quiet) AnsiConsole.MarkupLine("[blue]Running tests for blazor...[/]");

        if (showReport)
        {
            var reportDirectory = Path.Combine(BlazorTestsFolder, "test-results", "playwright-report");
            if (Directory.Exists(reportDirectory))
            {
                if (!quiet) AnsiConsole.MarkupLine("[blue]Cleaning up previous test report...[/]");
                Directory.Delete(reportDirectory, true);
            }
        }

        var showBrowser = headed || debug || slowMo;
        var runSequential = showBrowser || debugTiming;

        var playwrightArgs = BuildPlaywrightArgs(
            testPatterns, browser, debug, searchGrep, showBrowser, includeSlow, lastFailed, onlyChanged, repeatEach,
            retries, runSequential, smoke, stopOnFirstFailure, ui, workers
        );

        var command = BuildBlazorPlaywrightCommand(playwrightArgs);

        if (!quiet) AnsiConsole.MarkupLine($"[cyan]Running command in blazor: {Markup.Escape(command)}[/]");

        var environmentVariables = BuildPlaywrightEnvironmentVariables(BaseUrl, slowMo, debugTiming, LoadDefault("assertionTimeout"));

        // The working directory is blazor/, so the playwright.config.ts under blazor/tests and the Playwright CLI path resolve from there
        if (quiet)
        {
            var result = ProcessHelper.ExecuteQuietly(command, Configuration.BlazorFolder, environmentVariables);
            Console.WriteLine(ExtractPlaywrightSummary(result.CombinedOutput) ?? (result.Success ? "blazor: all tests passed." : "blazor: tests failed."));
            if (!result.Success) Console.WriteLine($"Full output: {result.TempFilePathWithSize}");
            return result.Success;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = BuildBlazorPlaywrightArguments(playwrightArgs),
            WorkingDirectory = Configuration.BlazorFolder,
            UseShellExecute = false
        };

        foreach (var (name, value) in environmentVariables)
        {
            processStartInfo.EnvironmentVariables[name] = value;
        }

        try
        {
            ProcessHelper.StartProcess(processStartInfo, throwOnError: true);
            AnsiConsole.MarkupLine("[green]Tests for blazor completed successfully[/]");
            return true;
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine("[red]Tests for blazor failed[/]");
            return false;
        }
    }

    private static (string[] testPatterns, string? grep) ProcessSearchTerms(string[] searchTerms)
    {
        if (searchTerms.Length == 0)
        {
            return ([], null);
        }

        var testPatterns = new List<string>();
        var grepTerms = new List<string>();

        foreach (var term in searchTerms)
        {
            var processedTerm = term;
            var wasAtSymbol = false;

            // Handle escaped @tag syntax from CommandLineArgumentsPreprocessor
            if (term.StartsWith(CommandLineArgumentsPreprocessor.EscapedAtSymbolMarker))
            {
                processedTerm = term.Substring(CommandLineArgumentsPreprocessor.EscapedAtSymbolMarker.Length);
                wasAtSymbol = true;
            }

            // If the term ends with .spec.ts or looks like a file, treat it as a test pattern
            if (processedTerm.EndsWith(".spec.ts") || processedTerm.Contains('/') || processedTerm.Contains('\\'))
            {
                testPatterns.Add(processedTerm);
            }
            else
            {
                // For grep terms, preserve the @ if it was originally there
                var grepTerm = wasAtSymbol ? $"@{processedTerm}" : processedTerm;
                grepTerms.Add(grepTerm);
            }
        }

        // Combine search terms
        var finalGrep = grepTerms.Count > 0 ? string.Join(" ", grepTerms) : null;

        return (testPatterns.ToArray(), finalGrep);
    }

    private static bool RunTestsCombined(
        string[] selfContainedSystems,
        string[] testPatterns,
        string browser,
        bool debugTiming,
        string? searchGrep,
        bool includeSlow,
        bool lastFailed,
        bool onlyChanged,
        int? repeatEach,
        int? retries,
        bool showReport,
        bool smoke,
        bool stopOnFirstFailure,
        int? workers,
        bool quiet)
    {
        // Build test directory arguments for all SCSs so Playwright runs everything in one invocation
        var testDirs = new List<string>();
        foreach (var system in selfContainedSystems)
        {
            var end2EndTestsPath = Path.Combine(system, "WebApp", "tests", "e2e");
            var fullPath = Path.Combine(Configuration.ApplicationFolder, end2EndTestsPath);
            if (!Directory.Exists(fullPath))
            {
                if (!quiet) AnsiConsole.MarkupLine($"[yellow]No end-to-end tests found for {system}. Skipping...[/]");
                continue;
            }

            testDirs.Add(end2EndTestsPath);
        }

        if (testDirs.Count == 0)
        {
            if (!quiet) AnsiConsole.MarkupLine("[yellow]No end-to-end tests found for any system.[/]");
            return true;
        }

        if (!quiet) AnsiConsole.MarkupLine($"[blue]Running tests for {string.Join(", ", selfContainedSystems)} in a single Playwright invocation...[/]");

        // Write a temporary combined Playwright config
        var combinedConfigPath = Path.Combine(Configuration.ApplicationFolder, "playwright.combined.config.ts");
        var testMatchEntries = string.Join(", ", testDirs.Select(dir => $"\"{dir}/**/*.spec.ts\""));
        var configContent = $$"""
                              import { defineConfig } from "@playwright/test";
                              import baseConfig from "./shared-webapp/tests/e2e/playwright.config";

                              export default defineConfig({
                                ...baseConfig,
                                testDir: ".",
                                testMatch: [{{testMatchEntries}}]
                              });
                              """;
        File.WriteAllText(combinedConfigPath, configContent);

        try
        {
            // Clean up report directory if we're going to show it
            var reportDirectory = Path.Combine(Configuration.ApplicationFolder, "tests", "test-results", "playwright-report");
            if (showReport && Directory.Exists(reportDirectory))
            {
                Directory.Delete(reportDirectory, true);
            }

            var runSequential = debugTiming;
            var isLocalhost = BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase);

            var playwrightArgs = BuildPlaywrightArgs(
                testPatterns, browser, false, searchGrep, false, includeSlow, lastFailed, onlyChanged, repeatEach,
                retries, runSequential, smoke, stopOnFirstFailure, false, workers
            );

            var command = $"{(Configuration.IsWindows ? "cmd.exe /C npx" : "npx")} playwright test --config=./playwright.combined.config.ts {playwrightArgs}";

            if (!quiet) AnsiConsole.MarkupLine($"[cyan]Running: npx playwright test --config=./playwright.combined.config.ts {playwrightArgs}[/]");

            var environmentVariables = new List<(string Name, string Value)> { ("PUBLIC_URL", BaseUrl), ("PLAYWRIGHT_HTML_OPEN", "never") };
            if (isLocalhost) environmentVariables.Add(("PLAYWRIGHT_VIDEO_MODE", "on"));
            if (debugTiming) environmentVariables.Add(("PLAYWRIGHT_SHOW_DEBUG_TIMING", "true"));
            var assertionTimeout = LoadDefault("assertionTimeout");
            if (assertionTimeout is not null) environmentVariables.Add(("PLAYWRIGHT_EXPECT_TIMEOUT", (assertionTimeout.Value * 1000).ToString()));
            var testTimeout = LoadDefault("testTimeout");
            if (testTimeout is not null) environmentVariables.Add(("PLAYWRIGHT_TIMEOUT", (testTimeout.Value * 1000).ToString()));

            if (quiet)
            {
                var result = ProcessHelper.ExecuteQuietly(command, Configuration.ApplicationFolder, environmentVariables.ToArray());
                Console.WriteLine(ExtractPlaywrightSummary(result.CombinedOutput) ?? (result.Success ? "All tests passed." : "Tests failed."));
                if (!result.Success) Console.WriteLine($"Full output: {result.TempFilePathWithSize}");
                return result.Success;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = Configuration.IsWindows ? "cmd.exe" : "npx",
                Arguments = $"{(Configuration.IsWindows ? "/C npx" : string.Empty)} playwright test --config=./playwright.combined.config.ts {playwrightArgs}",
                WorkingDirectory = Configuration.ApplicationFolder,
                UseShellExecute = false,
                Environment = { ["PUBLIC_URL"] = BaseUrl, ["PLAYWRIGHT_HTML_OPEN"] = "never" }
            };

            if (isLocalhost) processStartInfo.EnvironmentVariables["PLAYWRIGHT_VIDEO_MODE"] = "on";
            if (debugTiming) processStartInfo.EnvironmentVariables["PLAYWRIGHT_SHOW_DEBUG_TIMING"] = "true";

            try
            {
                ProcessHelper.StartProcess(processStartInfo, throwOnError: true);
                AnsiConsole.MarkupLine("[green]All tests completed successfully[/]");
                return true;
            }
            catch
            {
                AnsiConsole.MarkupLine("[red]Some tests failed[/]");
                return false;
            }
        }
        finally
        {
            // Clean up the temporary config
            if (File.Exists(combinedConfigPath)) File.Delete(combinedConfigPath);
        }
    }

    private static bool RunTestsForSystem(
        string selfContainedSystem,
        string[] testPatterns,
        string browser,
        bool debug,
        bool debugTiming,
        string? searchGrep,
        bool headed,
        bool includeSlow,
        bool lastFailed,
        bool onlyChanged,
        int? repeatEach,
        int? retries,
        bool showReport,
        bool slowMo,
        bool smoke,
        bool stopOnFirstFailure,
        bool ui,
        int? workers,
        bool quiet)
    {
        var systemPath = Path.Combine(Configuration.ApplicationFolder, selfContainedSystem, "WebApp");
        var end2EndTestsPath = Path.Combine(systemPath, "tests/e2e");

        if (!Directory.Exists(end2EndTestsPath))
        {
            if (!quiet) AnsiConsole.MarkupLine($"[yellow]No end-to-end tests found for {selfContainedSystem}. Skipping...[/]");
            return true;
        }

        if (!quiet) AnsiConsole.MarkupLine($"[blue]Running tests for {selfContainedSystem}...[/]");

        // Clean up report directory if we're going to show it
        if (showReport)
        {
            var reportDirectory = Path.Combine(systemPath, "tests", "test-results", "playwright-report");
            if (Directory.Exists(reportDirectory))
            {
                if (!quiet) AnsiConsole.MarkupLine("[blue]Cleaning up previous test report...[/]");
                Directory.Delete(reportDirectory, true);
            }
        }

        var showBrowser = headed || debug || slowMo;
        var runSequential = showBrowser || debugTiming;

        var playwrightArgs = BuildPlaywrightArgs(
            testPatterns, browser, debug, searchGrep, showBrowser, includeSlow, lastFailed, onlyChanged, repeatEach,
            retries, runSequential, smoke, stopOnFirstFailure, ui, workers
        );

        var command = BuildSelfContainedSystemPlaywrightCommand(playwrightArgs, Configuration.IsWindows);

        if (!quiet) AnsiConsole.MarkupLine($"[cyan]Running command in {selfContainedSystem}: npx playwright test --config=./tests/playwright.config.ts {playwrightArgs}[/]");

        var environmentVariables = BuildPlaywrightEnvironmentVariables(BaseUrl, slowMo, debugTiming, LoadDefault("assertionTimeout"));

        if (quiet)
        {
            var result = ProcessHelper.ExecuteQuietly(command, systemPath, environmentVariables);
            Console.WriteLine(ExtractPlaywrightSummary(result.CombinedOutput) ?? (result.Success ? $"{selfContainedSystem}: all tests passed." : $"{selfContainedSystem}: tests failed."));
            if (!result.Success) Console.WriteLine($"Full output: {result.TempFilePathWithSize}");
            return result.Success;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = Configuration.IsWindows ? "cmd.exe" : "npx",
            Arguments = $"{(Configuration.IsWindows ? "/C npx" : string.Empty)} playwright test --config=./tests/playwright.config.ts {playwrightArgs}",
            WorkingDirectory = systemPath,
            UseShellExecute = false
        };

        foreach (var (name, value) in environmentVariables)
        {
            processStartInfo.EnvironmentVariables[name] = value;
        }

        var testsFailed = false;
        try
        {
            ProcessHelper.StartProcess(processStartInfo, throwOnError: true);
            AnsiConsole.MarkupLine($"[green]Tests for {selfContainedSystem} completed successfully[/]");
        }
        catch (Exception)
        {
            testsFailed = true;
            AnsiConsole.MarkupLine($"[red]Tests for {selfContainedSystem} failed[/]");
        }

        return !testsFailed;
    }

    private static void CheckWebsiteAccessibility(string url, bool waitForAspire, bool quiet = false)
    {
        var maxAttempts = waitForAspire ? 6 : 1; // 6 * 5s = 30 seconds
        var retryDelaySeconds = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = HttpClient.Send(new HttpRequestMessage(HttpMethod.Head, url));

                if (response.IsSuccessStatusCode)
                {
                    if (!quiet) AnsiConsole.MarkupLine($"[green]Server is accessible at {url}[/]");
                    return;
                }

                if (attempt < maxAttempts)
                {
                    if (!quiet) AnsiConsole.MarkupLine($"[yellow]Server returned HTTP {(int)response.StatusCode} ({response.StatusCode}), retrying in {retryDelaySeconds}s... (attempt {attempt}/{maxAttempts})[/]");
                    Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
                }
            }
            catch (Exception exception)
            {
                var reason = exception.InnerException?.Message ?? exception.Message;

                if (attempt < maxAttempts)
                {
                    if (!quiet) AnsiConsole.MarkupLine($"[yellow]Server not ready ({reason}), retrying in {retryDelaySeconds}s... (attempt {attempt}/{maxAttempts})[/]");
                    Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
                }
            }
        }

        Console.WriteLine($"Server is not accessible at {url}. Please start AppHost before running '{Configuration.AliasName} e2e'.");
        Environment.Exit(1);
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            if (IsLoopbackHostName(context.DnsEndPoint.Host))
            {
                await socket.ConnectAsync([IPAddress.Loopback, IPAddress.IPv6Loopback], context.DnsEndPoint.Port, cancellationToken);
            }
            else
            {
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            }

            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string[] DetermineSystemsToTest(string[] testPatterns, string? grep, string[] availableSystems)
    {
        if ((testPatterns.Length == 0 || testPatterns[0] == "*") && string.IsNullOrEmpty(grep))
        {
            return availableSystems;
        }

        var matchingSystems = new HashSet<string>();

        foreach (var pattern in testPatterns.Where(p => p != "*"))
        {
            var normalizedPattern = pattern.EndsWith(".spec.ts") ? pattern : $"{pattern}.spec.ts";
            normalizedPattern = Path.GetFileName(normalizedPattern);

            foreach (var system in availableSystems)
            {
                var end2EndTestsPath = Path.Combine(Configuration.ApplicationFolder, system, "WebApp", "tests", "e2e");
                if (!Directory.Exists(end2EndTestsPath)) continue;

                var testFiles = Directory.GetFiles(end2EndTestsPath, "*.spec.ts", SearchOption.AllDirectories)
                    .Select(Path.GetFileName);

                if (testFiles.Any(file => file?.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase) == true))
                {
                    matchingSystems.Add(system);
                }
            }
        }

        if (matchingSystems.Count > 0) return matchingSystems.ToArray();

        if (!string.IsNullOrEmpty(grep))
        {
            foreach (var system in availableSystems)
            {
                var end2EndTestsPath = Path.Combine(Configuration.ApplicationFolder, system, "WebApp", "tests", "e2e");
                if (!Directory.Exists(end2EndTestsPath)) continue;

                var testFiles = Directory.GetFiles(end2EndTestsPath, "*.spec.ts", SearchOption.AllDirectories);
                foreach (var testFile in testFiles)
                {
                    // For filename search, remove @ if present for comparison
                    var filenameSearchTerm = grep.StartsWith("@") ? grep.Substring(1) : grep;
                    var fileName = Path.GetFileNameWithoutExtension(testFile);
                    if (fileName.Contains(filenameSearchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingSystems.Add(system);
                        break;
                    }

                    // For content search, use the grep term as-is (with @ if present)
                    if (File.ReadAllText(testFile).Contains(grep, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingSystems.Add(system);
                        break;
                    }
                }
            }

            return matchingSystems.ToArray();
        }

        return availableSystems;
    }

    private static void OpenHtmlReport(string selfContainedSystem)
    {
        var reportPath = Path.Combine(Configuration.ApplicationFolder, selfContainedSystem, "WebApp", "tests", "test-results", "playwright-report", "index.html");

        if (File.Exists(reportPath))
        {
            AnsiConsole.MarkupLine($"[green]Opening test report for '{selfContainedSystem}'...[/]");
            ProcessHelper.OpenBrowser(reportPath);
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No test report found for '{selfContainedSystem}' at '{reportPath}'[/]");
        }
    }

    private static void OpenBlazorHtmlReport()
    {
        var reportPath = Path.Combine(BlazorTestsFolder, "test-results", "playwright-report", "index.html");

        if (File.Exists(reportPath))
        {
            AnsiConsole.MarkupLine("[green]Opening test report for 'blazor'...[/]");
            ProcessHelper.OpenBrowser(reportPath);
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No test report found for 'blazor' at '{reportPath}'[/]");
        }
    }

    private static void OpenCombinedHtmlReport()
    {
        // The combined run uses the application-level report path from the base config (test-results/playwright-report)
        var reportPath = Path.Combine(Configuration.ApplicationFolder, "test-results", "playwright-report", "index.html");

        if (File.Exists(reportPath))
        {
            AnsiConsole.MarkupLine("[green]Opening combined test report...[/]");
            ProcessHelper.OpenBrowser(reportPath);
        }
        else
        {
            // Fall back to per-system reports
            foreach (var selfContainedSystem in AvailableSelfContainedSystems)
            {
                OpenHtmlReport(selfContainedSystem);
            }
        }
    }

    private static string? ExtractPlaywrightSummary(string output)
    {
        // Playwright summary lines: "8 failed" and "5 passed (31.3s)" near the end, possibly separated by test names
        // Match lines like "N passed", "N failed", "N passed (Xs)", "N skipped"
        var summaryPattern = SummaryLineRegex();
        var parts = new List<string>();

        foreach (var line in output.Split('\n').Reverse())
        {
            var cleaned = AnsiEscapeRegex().Replace(line, "").Trim();
            if (summaryPattern.IsMatch(cleaned))
            {
                parts.Insert(0, cleaned);
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    [GeneratedRegex(@"^\d+ (passed|failed|skipped)")]
    private static partial Regex SummaryLineRegex();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex AnsiEscapeRegex();

    private static void ConfigurePerformanceProfile()
    {
        var profiles = new Dictionary<string, (int Workers, int AssertionTimeout, int TestTimeout)>
        {
            ["High-end (8 workers, 15s assertions, 2m tests)"] = (8, 15, 120),
            ["Mid-range (6 workers, 20s assertions, 3m tests)"] = (6, 20, 180),
            ["Low-spec (4 workers, 30s assertions, 4m tests)"] = (4, 30, 240),
            ["CI runner (1 worker, 30s assertions, 4m tests)"] = (1, 30, 240)
        };

        var currentWorkers = LoadDefault("workers");
        var currentAssertionTimeout = LoadDefault("assertionTimeout");
        var currentTestTimeout = LoadDefault("testTimeout");

        if (currentWorkers is not null)
        {
            AnsiConsole.MarkupLine($"[blue]Current settings: {currentWorkers} workers, {currentAssertionTimeout ?? 20}s assertions, {currentTestTimeout ?? 180}s tests[/]");
        }

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]performance profile[/] for this machine:")
                .AddChoices(profiles.Keys)
        );

        var (workers, assertionTimeout, testTimeout) = profiles[selection];
        SaveDefaults("workers", workers);
        SaveDefaults("assertionTimeout", assertionTimeout);
        SaveDefaults("testTimeout", testTimeout);

        AnsiConsole.MarkupLine($"[green]Profile saved: {workers} workers, {assertionTimeout}s assertion timeout, {testTimeout}s test timeout[/]");
    }

    private static void SaveDefaults(string key, int value)
    {
        var directory = Path.GetDirectoryName(DefaultsFilePath)!;
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        var defaults = LoadAllDefaults();
        defaults[key] = value;

        var entries = defaults.Select(kvp => $"\"{kvp.Key}\":{kvp.Value}");
        File.WriteAllText(DefaultsFilePath, $"{{{string.Join(",", entries)}}}");
    }

    private static int? LoadDefault(string key)
    {
        var defaults = LoadAllDefaults();
        return defaults.TryGetValue(key, out var value) ? value : null;
    }

    private static Dictionary<string, int> LoadAllDefaults()
    {
        if (!File.Exists(DefaultsFilePath)) return new Dictionary<string, int>();

        try
        {
            var json = File.ReadAllText(DefaultsFilePath);
            var result = new Dictionary<string, int>();
            foreach (Match match in Regex.Matches(json, "\"(\\w+)\":(\\d+)"))
            {
                result[match.Groups[1].Value] = int.Parse(match.Groups[2].Value);
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, int>();
        }
    }

    private static void DeleteAllTestArtifacts(bool blazor)
    {
        AnsiConsole.MarkupLine("[blue]Deleting test artifacts...[/]");

        var totalDeleted = 0;

        foreach (var selfContainedSystemName in AvailableSelfContainedSystems)
        {
            var testResultsDirectory = Path.Combine(Configuration.ApplicationFolder, selfContainedSystemName, "WebApp", "tests", "test-results");

            if (!Directory.Exists(testResultsDirectory)) continue;

            Directory.Delete(testResultsDirectory, true);
            totalDeleted++;
            AnsiConsole.MarkupLine($"[green]Deleted test-results directory for {selfContainedSystemName}[/]");
        }

        var blazorTestResultsDirectory = Path.Combine(BlazorTestsFolder, "test-results");
        if (blazor && Directory.Exists(blazorTestResultsDirectory))
        {
            Directory.Delete(blazorTestResultsDirectory, true);
            totalDeleted++;
            AnsiConsole.MarkupLine("[green]Deleted test-results directory for blazor[/]");
        }

        AnsiConsole.MarkupLine(totalDeleted > 0
            ? $"[green]Successfully deleted test artifacts from {totalDeleted} system(s)[/]"
            : "[yellow]No test artifacts found to delete[/]"
        );
    }
}
