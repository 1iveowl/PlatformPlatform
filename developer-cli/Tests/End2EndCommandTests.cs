using DeveloperCli.Commands;
using FluentAssertions;

namespace DeveloperCli.Tests;

public sealed class End2EndCommandTests
{
    [Fact]
    public void ValidateBlazorOptions_WhenBlazorWithSelfContainedSystem_ShouldReturnError()
    {
        // Act
        var error = End2EndCommand.ValidateBlazorOptions(true, "account");

        // Assert
        error.Should().Be("--blazor cannot be combined with --self-contained-system.");
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "account")]
    [InlineData(false, null)]
    public void ValidateBlazorOptions_WhenNotCombined_ShouldReturnNoError(bool blazor, string? selfContainedSystem)
    {
        // Act
        var error = End2EndCommand.ValidateBlazorOptions(blazor, selfContainedSystem);

        // Assert
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("https://app.dev.localhost:9000", false, "https://app.dev.localhost:9000")]
    [InlineData("https://app.dev.localhost:9000", true, "https://app.dev.localhost:9000/blazor/")]
    [InlineData("https://app.dev.localhost:9000/", true, "https://app.dev.localhost:9000/blazor/")]
    public void GetServerCheckUrl_ShouldProbeTheBlazorPathBaseOnlyForBlazor(string baseUrl, bool blazor, string expected)
    {
        // Act
        var url = End2EndCommand.GetServerCheckUrl(baseUrl, blazor);

        // Assert
        url.Should().Be(expected);
    }

    [Fact]
    public void BuildPlaywrightArgs_WhenDefaults_ShouldSelectChromiumAndExcludeSlowTests()
    {
        // Act
        var args = End2EndCommand.BuildPlaywrightArgs([], "chromium", false, null, false, false, false, false, null, null, false, false, false, false, null);

        // Assert
        args.Should().Be("--project=chromium --grep-invert=\"@slow\"");
    }

    [Fact]
    public void BuildPlaywrightArgs_WhenSmokeIncludeSlowLastFailedAndStopOnFirstFailure_ShouldAddEachOption()
    {
        // Act
        var args = End2EndCommand.BuildPlaywrightArgs([], "chromium", false, null, false, true, true, false, null, null, false, true, true, false, null);

        // Assert
        args.Should().Be("--project=chromium --grep=\"@smoke\" --last-failed -x");
    }

    [Theory]
    [InlineData("firefox", "--project=firefox --grep-invert=\"@slow\"")]
    [InlineData("safari", "--project=webkit --grep-invert=\"@slow\"")]
    [InlineData("all", "--grep-invert=\"@slow\"")]
    public void BuildPlaywrightArgs_WhenBrowser_ShouldMapToPlaywrightProject(string browser, string expected)
    {
        // Act
        var args = End2EndCommand.BuildPlaywrightArgs([], browser, false, null, false, false, false, false, null, null, false, false, false, false, null);

        // Assert
        args.Should().Be(expected);
    }

    [Theory]
    [InlineData("chromium", false, false, "--project=chromium-* --grep-invert=\"@slow\"")]
    [InlineData("firefox", true, false, "--project=firefox-* --grep=\"@smoke\" --grep-invert=\"@slow\"")]
    [InlineData("safari", false, true, "--project=webkit-*")]
    [InlineData("all", true, true, "--grep=\"@smoke\"")]
    public void BuildPlaywrightArgs_WhenBlazor_ShouldSelectEveryCultureAndLaneProjectOfTheBrowser(string browser, bool smoke, bool includeSlow, string expected)
    {
        // Act
        var args = End2EndCommand.BuildPlaywrightArgs([], browser, false, null, false, includeSlow, false, false, null, null, false, smoke, false, false, null, true);

        // Assert
        args.Should().Be(expected);
    }

    [Fact]
    public void BuildPlaywrightArgs_WhenPatternsGrepAndExecutionOptions_ShouldKeepTheirOrder()
    {
        // Act
        var args = End2EndCommand.BuildPlaywrightArgs(
            ["login.spec.ts", "./tests/e2e/signup.spec.ts"], "chromium", true, "@comprehensive", true, false, false, true, 3, 0, true, false, false, true, null
        );

        // Assert
        args.Should().Be("--project=chromium ./tests/e2e/login.spec.ts ./tests/e2e/signup.spec.ts --grep=\"@comprehensive\" --grep-invert=\"@slow\" --ui --debug --headed --only-changed --repeat-each=3 --retries=0 --workers=1");
    }

    [Fact]
    public void BuildPlaywrightArgs_WhenWorkersGiven_ShouldOverrideSequentialRun()
    {
        // Act
        var args = End2EndCommand.BuildPlaywrightArgs([], "chromium", false, null, false, true, false, false, null, null, true, false, false, false, 4);

        // Assert
        args.Should().Be("--project=chromium --workers=4");
    }

    [Theory]
    [InlineData(false, "npx playwright test --config=./tests/playwright.config.ts --project=chromium --grep-invert=\"@slow\"")]
    [InlineData(true, "cmd.exe /C npx playwright test --config=./tests/playwright.config.ts --project=chromium --grep-invert=\"@slow\"")]
    public void BuildSelfContainedSystemPlaywrightCommand_ShouldRunNpxPlaywrightWithTheSystemConfig(bool isWindows, string expected)
    {
        // Act
        var command = End2EndCommand.BuildSelfContainedSystemPlaywrightCommand("--project=chromium --grep-invert=\"@slow\"", isWindows);

        // Assert
        command.Should().Be(expected);
    }

    [Fact]
    public void BuildBlazorPlaywrightCommand_ShouldRunThePinnedPlaywrightCliWithTheBlazorConfig()
    {
        // Act
        var command = End2EndCommand.BuildBlazorPlaywrightCommand("--project=chromium --grep=\"@smoke\" --grep-invert=\"@slow\"");

        // Assert
        command.Should().Be("node ../application/node_modules/@playwright/test/cli.js test --config=./tests/playwright.config.ts --project=chromium --grep=\"@smoke\" --grep-invert=\"@slow\"");
    }

    [Fact]
    public void BuildPlaywrightEnvironmentVariables_WhenLocalhostWithAllOptions_ShouldSetEveryVariableInOrder()
    {
        // Act
        var environmentVariables = End2EndCommand.BuildPlaywrightEnvironmentVariables("https://app.dev.localhost:9000", true, true, 20);

        // Assert
        environmentVariables.Should().Equal(
            ("PUBLIC_URL", "https://app.dev.localhost:9000"),
            ("PLAYWRIGHT_HTML_OPEN", "never"),
            ("PLAYWRIGHT_SLOW_MO", "500"),
            ("PLAYWRIGHT_VIDEO_MODE", "on"),
            ("PLAYWRIGHT_SHOW_DEBUG_TIMING", "true"),
            ("PLAYWRIGHT_EXPECT_TIMEOUT", "20000")
        );
    }

    [Fact]
    public void BuildPlaywrightEnvironmentVariables_WhenRemoteWithoutOptions_ShouldSetOnlyUrlAndReportMode()
    {
        // Act
        var environmentVariables = End2EndCommand.BuildPlaywrightEnvironmentVariables("https://staging.example.com", false, false, null);

        // Assert
        environmentVariables.Should().Equal(("PUBLIC_URL", "https://staging.example.com"), ("PLAYWRIGHT_HTML_OPEN", "never"));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("app.dev.localhost")]
    [InlineData("back-office.dev.localhost")]
    [InlineData("APP.DEV.LOCALHOST.")]
    public void IsLoopbackHostName_WhenLocalhostName_ShouldReturnTrue(string hostName)
    {
        // Act
        var isLoopback = End2EndCommand.IsLoopbackHostName(hostName);

        // Assert
        isLoopback.Should().BeTrue();
    }

    [Theory]
    [InlineData("staging.example.com")]
    [InlineData("localhost.example.com")]
    [InlineData("notlocalhost")]
    public void IsLoopbackHostName_WhenOtherName_ShouldReturnFalse(string hostName)
    {
        // Act
        var isLoopback = End2EndCommand.IsLoopbackHostName(hostName);

        // Assert
        isLoopback.Should().BeFalse();
    }
}
