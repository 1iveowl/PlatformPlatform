using System.Globalization;
using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class InstallPromptPolicyTests
{
    private const string IosPhoneUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";
    private const string IosTabletUserAgent = "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";
    private const string MacUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";
    private const string AndroidUserAgent = "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Mobile Safari/537.36";
    private const string WindowsUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36";

    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(IosPhoneUserAgent, 5, true)]
    [InlineData(IosTabletUserAgent, 5, true)]
    [InlineData(MacUserAgent, 5, true)]
    [InlineData(MacUserAgent, 0, false)]
    [InlineData(MacUserAgent, 1, false)]
    [InlineData(AndroidUserAgent, 5, false)]
    [InlineData(WindowsUserAgent, 10, false)]
    [InlineData(null, 5, false)]
    [InlineData("", 0, false)]
    public void IsIos_WhenUserAgentAndTouchPointsGiven_ShouldDetectIosDevices(string? userAgent, int maxTouchPoints, bool expected)
    {
        // Act
        var isIos = InstallPromptPolicy.IsIos(userAgent, maxTouchPoints);

        // Assert
        isIos.Should().Be(expected);
    }

    [Fact]
    public void ShouldShow_WhenIosAndNeverDismissed_ShouldBeTrue()
    {
        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment(), Now);

        // Assert
        show.Should().BeTrue();
    }

    [Theory]
    [InlineData(AndroidUserAgent, 5)]
    [InlineData(MacUserAgent, 0)]
    [InlineData(WindowsUserAgent, 10)]
    public void ShouldShow_WhenNotIos_ShouldBeFalse(string userAgent, int maxTouchPoints)
    {
        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment(userAgent, maxTouchPoints), Now);

        // Assert
        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_WhenStandalone_ShouldBeFalse()
    {
        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment() with { IsStandalone = true }, Now);

        // Assert
        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_WhenDismissedForSession_ShouldBeFalse()
    {
        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment() with { DismissedForSession = true }, Now);

        // Assert
        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_WhenDismissedWithinSevenDays_ShouldBeFalse()
    {
        // Arrange
        var dismissedUntil = InstallPromptPolicy.FormatDismissedUntil(Now.AddDays(-6));

        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment() with { DismissedUntil = dismissedUntil }, Now);

        // Assert
        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_WhenDismissedJustNow_ShouldBeFalse()
    {
        // Arrange
        var dismissedUntil = InstallPromptPolicy.FormatDismissedUntil(Now);

        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment() with { DismissedUntil = dismissedUntil }, Now);

        // Assert
        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_WhenDismissalHasEnded_ShouldBeTrue()
    {
        // Arrange
        var dismissedUntil = InstallPromptPolicy.FormatDismissedUntil(Now.AddDays(-7));

        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment() with { DismissedUntil = dismissedUntil }, Now);

        // Assert
        show.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    [InlineData("")]
    public void ShouldShow_WhenStoredDismissalIsMalformed_ShouldBeTrue(string dismissedUntil)
    {
        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment() with { DismissedUntil = dismissedUntil }, Now);

        // Assert
        show.Should().BeTrue();
    }

    [Fact]
    public void ShouldShow_WhenStoredDismissalEndsBeyondTheDismissalPeriod_ShouldBeTrue()
    {
        // Arrange
        var dismissedUntil = Now.AddDays(30).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        // Act
        var show = InstallPromptPolicy.ShouldShow(Environment() with { DismissedUntil = dismissedUntil }, Now);

        // Assert
        show.Should().BeTrue();
    }

    [Fact]
    public void FormatDismissedUntil_WhenGivenNow_ShouldBeUnixMillisecondsSevenDaysLater()
    {
        // Act
        var value = InstallPromptPolicy.FormatDismissedUntil(Now);

        // Assert
        value.Should().Be(Now.AddDays(7).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
    }

    private static InstallPromptEnvironment Environment(string userAgent = IosPhoneUserAgent, int maxTouchPoints = 5)
    {
        return new InstallPromptEnvironment(userAgent, maxTouchPoints, false, null, false);
    }
}
