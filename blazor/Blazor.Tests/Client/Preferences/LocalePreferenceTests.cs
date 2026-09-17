using Blazor.Client.Preferences;
using FluentAssertions;

namespace Blazor.Tests.Client.Preferences;

public sealed class LocalePreferenceTests
{
    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("da-DK", "da-DK")]
    [InlineData("da-dk", "da-DK")]
    [InlineData("da", null)]
    [InlineData("fr-FR", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("da-DK%3B", null)]
    public void Parse_WhenCookieValueGiven_ShouldAcceptOnlyAnExactlySupportedLocale(string? cookieValue, string? expected)
    {
        // Act
        var locale = LocalePreference.Parse(cookieValue);

        // Assert
        locale.Should().Be(expected);
    }

    [Fact]
    public void Cookie_WhenDescribed_ShouldHaveThePreferredTenantCookieLifetime()
    {
        // Assert
        LocalePreference.CookieName.Should().Be("preferred-locale");
        LocalePreference.MaxAge.Should().Be(TimeSpan.FromDays(365));
        LocalePreference.Locales.Should().Equal("en-US", "da-DK");
    }

    [Fact]
    public void Label_WhenEveryLocaleLabelled_ShouldUseTheLanguagesOwnName()
    {
        // Assert
        LocalePreference.Label("en-US").Should().Be("English");
        LocalePreference.Label("da-DK").Should().Be("Dansk");
    }
}
