using System.CommandLine;
using DeveloperCli.Installation;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Commands;

public sealed class BlazorHarnessCommand : Command
{
    private static readonly string[] Browsers = ["chromium", "firefox", "webkit"];
    private static readonly string TestsFolder = Path.Combine(Configuration.BlazorFolder, "tests");

    public BlazorHarnessCommand() : base("blazor-harness", "Runs a browser harness script from blazor/tests against the running stack, one browser at a time")
    {
        var scriptArgument = new Argument<string>("script") { Description = "The harness script name without extension (e.g., trimmed-smoke, public-pages, shell-policy)" };
        var browserOption = new Option<string>("--browser", "-b") { Description = "chromium, firefox, webkit or all. Defaults to chromium", DefaultValueFactory = _ => "chromium" };

        Arguments.Add(scriptArgument);
        Options.Add(browserOption);

        // Further options are passed to the script unchanged, for example --environment production
        TreatUnmatchedTokensAsErrors = false;

        SetAction(parseResult => Execute(parseResult.GetValue(scriptArgument)!, parseResult.GetValue(browserOption)!, parseResult.UnmatchedTokens.ToArray()));
    }

    private static void Execute(string script, string browser, string[] scriptArguments)
    {
        Prerequisite.Ensure(Prerequisite.Node);

        var scriptFile = Path.Combine(TestsFolder, $"{script}.mjs");
        if (!File.Exists(scriptFile))
        {
            var available = Directory.GetFiles(TestsFolder, "*.mjs").Select(Path.GetFileNameWithoutExtension);
            AnsiConsole.MarkupLine($"[red]No harness script '{Markup.Escape(script)}'. Available: {string.Join(", ", available)}[/]");
            Environment.Exit(1);
        }

        string[] selectedBrowsers = browser == "all" ? Browsers : [browser];
        if (selectedBrowsers.Any(selected => !Browsers.Contains(selected)))
        {
            AnsiConsole.MarkupLine($"[red]Unknown browser '{Markup.Escape(browser)}'. Use chromium, firefox, webkit or all.[/]");
            Environment.Exit(1);
        }

        var failedBrowsers = new List<string>();
        foreach (var selectedBrowser in selectedBrowsers)
        {
            AnsiConsole.MarkupLine($"[blue]Running {Markup.Escape(script)} in {selectedBrowser}...[/]");
            try
            {
                var extraArguments = scriptArguments.Length == 0 ? "" : " " + string.Join(" ", scriptArguments);
                ProcessHelper.StartProcess($"node blazor/tests/{script}.mjs --browser {selectedBrowser}{extraArguments}", Configuration.SourceCodeFolder, throwOnError: true);
            }
            catch (ProcessExecutionException)
            {
                failedBrowsers.Add(selectedBrowser);
            }
        }

        if (failedBrowsers.Count > 0)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(script)} failed in {string.Join(", ", failedBrowsers)}[/]");
            Environment.Exit(1);
        }

        AnsiConsole.MarkupLine($"[green]{Markup.Escape(script)} passed in {string.Join(", ", selectedBrowsers)}[/]");
    }
}
