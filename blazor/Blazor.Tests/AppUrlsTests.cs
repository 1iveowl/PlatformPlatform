using Blazor.Client;
using FluentAssertions;

namespace Blazor.Tests;

public sealed class AppUrlsTests
{
    [Theory]
    [InlineData("app", "/blazor/app")]
    [InlineData("/app", "/blazor/app")]
    [InlineData("./_framework/blazor.web.js", "/blazor/_framework/blazor.web.js")]
    [InlineData("", "/blazor/")]
    public void ToAbsolute_WhenRelativeOrRooted_ShouldPrefixPathBase(string url, string expected)
    {
        // Act
        var absolute = AppUrls.ToAbsolute(url);

        // Assert
        absolute.Should().Be(expected);
    }

    [Theory]
    [InlineData("/blazor/app")]
    [InlineData("/blazor")]
    [InlineData("/blazor?returnPath=%2Fblazor%2Fapp")]
    public void ToAbsolute_WhenAlreadyUnderPathBase_ShouldPrefixOnce(string url)
    {
        // Act
        var absolute = AppUrls.ToAbsolute(url);

        // Assert
        absolute.Should().Be(url);
    }

    [Fact]
    public void ToAbsolute_WhenAppliedTwice_ShouldPrefixOnce()
    {
        // Act
        var absolute = AppUrls.ToAbsolute(AppUrls.ToAbsolute("app/details"));

        // Assert
        absolute.Should().Be("/blazor/app/details");
    }

    [Theory]
    [InlineData("login?returnPath=%2Fblazor%2Fapp#top", "/blazor/login?returnPath=%2Fblazor%2Fapp#top")]
    [InlineData("app/users/quick?search=ann", "/blazor/app/users/quick?search=ann")]
    public void ToAbsolute_WhenQueryOrFragment_ShouldKeepThem(string url, string expected)
    {
        // Act
        var absolute = AppUrls.ToAbsolute(url);

        // Assert
        absolute.Should().Be(expected);
    }

    [Fact]
    public void ToAbsolute_WhenAbsoluteHttpsUrl_ShouldReturnUnchanged()
    {
        // Arrange
        const string url = "https://cdn.example.com/app.css";

        // Act
        var absolute = AppUrls.ToAbsolute(url);

        // Assert
        absolute.Should().Be(url);
    }

    [Fact]
    public void ToAbsolute_WhenBlazorPrefixIsPartOfAnotherSegment_ShouldPrefixPathBase()
    {
        // Act
        var absolute = AppUrls.ToAbsolute("/blazor-other/app");

        // Assert
        absolute.Should().Be("/blazor/blazor-other/app");
    }

    [Theory]
    [InlineData("/blazor/app/details?tab=1")]
    [InlineData("/blazor/app/users/quick#row-3")]
    public void SanitizeReturnPath_WhenLocalPathUnderPathBase_ShouldKeepIt(string returnPath)
    {
        // Act
        var sanitized = AppUrls.SanitizeReturnPath(returnPath);

        // Assert
        sanitized.Should().Be(returnPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.example.com/blazor/app")]
    [InlineData("/blazor//evil.example.com")]
    [InlineData("/blazor/\\evil.example.com")]
    [InlineData("\\\\evil.example.com")]
    [InlineData("https://evil.example.com/blazor/app")]
    [InlineData("/dashboard")]
    [InlineData("/blazor")]
    [InlineData("/blazor-other/app")]
    [InlineData("/blazorx/app")]
    [InlineData("/blazor/../dashboard")]
    [InlineData("/blazor/./../dashboard")]
    [InlineData("/blazor/%2e%2e/dashboard")]
    [InlineData("/blazor/%2E%2E%2Fdashboard")]
    [InlineData("/blazor/..%2fdashboard")]
    [InlineData("/blazor/%2f%2fevil.example.com")]
    [InlineData("/blazor/%5cevil.example.com")]
    [InlineData("/blazor/\t/evil.example.com")]
    [InlineData("/blazor/app\n")]
    [InlineData("/blazor/app%")]
    [InlineData("/blazor/app%zz")]
    [InlineData("/dashboard?returnPath=/blazor/app")]
    [InlineData("/blazor/https://evil.example.com")]
    public void SanitizeReturnPath_WhenNotALocalPathUnderPathBase_ShouldReturnAuthenticatedHome(string? returnPath)
    {
        // Act
        var sanitized = AppUrls.SanitizeReturnPath(returnPath);

        // Assert
        sanitized.Should().Be("/blazor/app");
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("app.dev.localhost", true)]
    [InlineData("APP.DEV.LOCALHOST", true)]
    [InlineData("localhost.example.com", false)]
    [InlineData("platformplatform.net", false)]
    [InlineData(null, false)]
    public void IsLocalhost_WhenRequestHost_ShouldOnlyAcceptLocalhostAndItsSubdomains(string? host, bool expected)
    {
        // Act
        var isLocalhost = AppUrls.IsLocalhost(host);

        // Assert
        isLocalhost.Should().Be(expected);
    }
}
