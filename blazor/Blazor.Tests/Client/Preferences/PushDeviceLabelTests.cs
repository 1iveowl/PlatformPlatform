using Blazor.Client.Preferences;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Preferences;

// The label stored beside a subscription names a browser and a platform and nothing else. An agent string is never stored
// verbatim, so an agent this does not recognise becomes the unknown browser rather than the agent itself.
public sealed class PushDeviceLabelTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36", "Chrome on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15", "Safari on macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "Safari on iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    public void Create_WhenTheAgentNamesABrowserAndAPlatform_ShouldJoinThem(string userAgent, string expected)
    {
        // Assert
        PushDeviceLabel.Create(userAgent).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a-custom-agent/1.0")]
    public void Create_WhenTheAgentNamesNothingKnown_ShouldNameTheUnknownBrowserAndStoreNothingOfTheAgent(string? userAgent)
    {
        // Act
        var label = PushDeviceLabel.Create(userAgent);

        // Assert
        label.Should().Be(AccountStrings.NotificationsUnknownBrowser);
    }
}
