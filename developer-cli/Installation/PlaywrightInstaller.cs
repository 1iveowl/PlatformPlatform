using System.Diagnostics;
using DeveloperCli.Utilities;
using Spectre.Console;

namespace DeveloperCli.Installation;

public static class PlaywrightInstaller
{
    public static void EnsurePlaywrightBrowsers(bool quiet = false)
    {
        if (!quiet) AnsiConsole.MarkupLine("[blue]Ensuring Playwright browsers are installed...[/]");

        var command = Configuration.IsWindows
            ? "cmd.exe"
            : Configuration.IsLinux
                ? "sudo"
                : "npx";
        // sudo replaces PATH with its secure_path, which does not contain npx when Node is installed outside the system
        // folders (as in the dev container), so the caller's PATH is passed through
        var arguments = Configuration.IsWindows
            ? "/C npx --yes playwright install --with-deps"
            : Configuration.IsLinux
                ? $"env \"PATH={Environment.GetEnvironmentVariable("PATH")}\" npx --yes playwright install --with-deps"
                : "--yes playwright install --with-deps";

        if (quiet)
        {
            ProcessHelper.ExecuteQuietly($"{command} {arguments}", Configuration.ApplicationFolder);
        }
        else
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = Configuration.ApplicationFolder,
                UseShellExecute = false
            };

            ProcessHelper.StartProcess(processStartInfo, throwOnError: true);
        }
    }
}
