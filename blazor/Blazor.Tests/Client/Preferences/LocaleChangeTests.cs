using Blazor.Client.Preferences;
using FluentAssertions;

namespace Blazor.Tests.Client.Preferences;

public sealed class LocaleChangeTests
{
    [Theory]
    [InlineData("da-DK", "da-DK")]
    [InlineData("fr-FR", "en-US")]
    [InlineData("", "en-US")]
    public void Constructor_WhenDocumentCultureGiven_ShouldStartOnTheSupportedCultureOrTheDefault(string documentCulture, string expected)
    {
        // Act
        var change = new LocaleChange(documentCulture);

        // Assert
        change.CurrentLocale.Should().Be(expected);
        change.IsPending.Should().BeFalse();
    }

    [Fact]
    public void TryBegin_WhenAnotherSupportedLocaleChosen_ShouldMarkItPending()
    {
        // Arrange
        var change = new LocaleChange("en-US");

        // Act
        var started = change.TryBegin("da-DK");

        // Assert
        started.Should().BeTrue();
        change.PendingLocale.Should().Be("da-DK");
        change.CurrentLocale.Should().Be("en-US");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("")]
    public void TryBegin_WhenTheCurrentOrAnUnsupportedLocaleChosen_ShouldStartNothing(string requested)
    {
        // Arrange
        var change = new LocaleChange("en-US");

        // Act
        var started = change.TryBegin(requested);

        // Assert
        started.Should().BeFalse();
        change.IsPending.Should().BeFalse();
    }

    [Fact]
    public void TryBegin_WhenAChangeIsPending_ShouldKeepTheFirstChoice()
    {
        // Arrange
        var change = new LocaleChange("da-DK");
        change.TryBegin("en-US");

        // Act
        var started = change.TryBegin("da-DK");

        // Assert
        started.Should().BeFalse();
        change.PendingLocale.Should().Be("en-US");
    }

    [Fact]
    public void Fail_WhenPendingChangeRefused_ShouldKeepTheCurrentLocaleAndAllowARetry()
    {
        // Arrange
        var change = new LocaleChange("en-US");
        change.TryBegin("da-DK");

        // Act
        change.Fail();
        var retried = change.TryBegin("da-DK");

        // Assert
        change.CurrentLocale.Should().Be("en-US");
        change.Revision.Should().Be(1);
        retried.Should().BeTrue();
    }
}
