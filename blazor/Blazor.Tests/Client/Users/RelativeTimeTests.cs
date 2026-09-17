using System.Globalization;
using Blazor.Client.Components;
using FluentAssertions;

namespace Blazor.Tests.Client.Users;

// The created and last seen cells show recent times as the React edition's SmartDate does, in both cultures
public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en-US", 0, "Just now")]
    [InlineData("en-US", 59, "Just now")]
    [InlineData("en-US", -30, "Just now")]
    [InlineData("en-US", 60, "1 minute ago")]
    [InlineData("en-US", 119, "1 minute ago")]
    [InlineData("en-US", 120, "2 minutes ago")]
    [InlineData("en-US", 3599, "59 minutes ago")]
    [InlineData("en-US", 3600, "1 hour ago")]
    [InlineData("en-US", 7200, "2 hours ago")]
    [InlineData("en-US", 86399, "23 hours ago")]
    [InlineData("da-DK", 30, "Lige nu")]
    [InlineData("da-DK", 60, "1 minut siden")]
    [InlineData("da-DK", 300, "5 minutter siden")]
    [InlineData("da-DK", 3600, "1 time siden")]
    [InlineData("da-DK", 10800, "3 timer siden")]
    public void Format_WhenLessThanADayAgo_ShouldShowRelativeText(string culture, int secondsAgo, string expected)
    {
        // Arrange
        var value = Now.AddSeconds(-secondsAgo);

        // Act
        var text = WithCulture(culture, () => RelativeTime.Format(value, Now));

        // Assert
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    public void Format_WhenADayOrMoreAgo_ShouldShowTheShortLocalDate(string culture)
    {
        // Arrange
        var value = Now.AddDays(-1);

        // Act
        var text = WithCulture(culture, () => RelativeTime.Format(value, Now));

        // Assert
        text.Should().Be(value.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void Format_WhenValueIsNull_ShouldBeEmpty()
    {
        // Act
        var text = RelativeTime.Format(null, Now);

        // Assert
        text.Should().BeEmpty();
    }

    private static string WithCulture(string culture, Func<string> action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
