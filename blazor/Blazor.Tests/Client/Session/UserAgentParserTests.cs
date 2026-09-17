using Blazor.Client.Sessions;
using FluentAssertions;

namespace Blazor.Tests.Client.Session;

public sealed class UserAgentParserTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36", "Chrome", "Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0", "Edge", "Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 OPR/120.0.0.0", "Opera", "macOS")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:142.0) Gecko/20100101 Firefox/142.0", "Firefox", "Linux")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Safari/605.1.15", "Safari", "macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Mobile/15E148 Safari/604.1", "Safari", "iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36", "Chrome", "Android")]
    public void Parse_WhenUserAgentNamesABrowserAndSystem_ShouldPreferTheMostSpecificToken(string userAgent, string browser, string operatingSystem)
    {
        // Act
        var parsed = UserAgentParser.Parse(userAgent);

        // Assert
        parsed.Should().Be(new ParsedUserAgent(browser, operatingSystem));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curl/8.5.0")]
    public void Parse_WhenUserAgentNamesNothingKnown_ShouldReturnNulls(string? userAgent)
    {
        // Act
        var parsed = UserAgentParser.Parse(userAgent);

        // Assert
        parsed.Should().Be(new ParsedUserAgent(null, null));
    }
}
