using Account.Features.EmailAuthentication.Domain;
using Blazor.Host.Components.Pages.Public;
using FluentAssertions;

namespace Blazor.Tests.Public;

public sealed class VerificationFlowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void FromQuery_WhenJustSent_ShouldShowFullValidityAndHideResend()
    {
        // Act
        var flow = VerificationFlow.FromQuery("1800000000", "300", Now);

        // Assert
        flow.GetRemainingSeconds(Now).Should().Be(300);
        flow.GetResendInSeconds(Now).Should().Be(30);
    }

    [Fact]
    public void FromQuery_WhenTimePassed_ShouldCountDownAndRevealResendAfterThirtySeconds()
    {
        // Arrange
        var flow = VerificationFlow.FromQuery("1800000000", "300", Now);

        // Act & Assert
        flow.GetRemainingSeconds(Now.AddSeconds(29)).Should().Be(271);
        flow.GetResendInSeconds(Now.AddSeconds(29)).Should().Be(1);
        flow.GetResendInSeconds(Now.AddSeconds(30)).Should().Be(0);
        flow.GetRemainingSeconds(Now.AddSeconds(301)).Should().Be(0);
    }

    [Theory]
    [InlineData("1800000900", "300")]
    [InlineData("1800000000", "86400")]
    public void FromQuery_WhenEditedToExtendValidity_ShouldNeverShowMoreThanOneCodeGrants(string sent, string validFor)
    {
        // Act
        var flow = VerificationFlow.FromQuery(sent, validFor, Now);

        // Assert
        flow.GetRemainingSeconds(Now).Should().BeLessThanOrEqualTo(VerificationFlow.MaximumValidForSeconds);
        flow.SentAtUnixSeconds.Should().BeLessThanOrEqualTo(Now.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("soon", "300")]
    [InlineData("1800000000", "-5")]
    public void FromQuery_WhenStateMissingOrUnreadable_ShouldShowExpiredWithResend(string? sent, string? validFor)
    {
        // Act
        var flow = VerificationFlow.FromQuery(sent, validFor, Now);

        // Assert
        flow.GetRemainingSeconds(Now).Should().Be(0);
        flow.GetResendInSeconds(Now).Should().Be(0);
    }

    [Theory]
    [InlineData(300, "5:00")]
    [InlineData(61, "1:01")]
    [InlineData(9, "0:09")]
    public void FormatDuration_WhenSeconds_ShouldFormatMinutesAndSeconds(int seconds, string expected)
    {
        // Act & Assert
        VerificationFlow.FormatDuration(seconds).Should().Be(expected);
    }

    [Fact]
    public void GetVerifyUrl_WhenReturnPathIsOffSite_ShouldCarrySanitizedStateInQuery()
    {
        // Arrange
        var flow = VerificationFlow.Start(Now, 300);

        // Act
        var url = flow.GetVerifyUrl("login/verify", new EmailLoginId("emlog_01JMVAW4T4320KJ3A7EJMCG8R0"), "a+b@example.com", "https://evil.example.com", true);

        // Assert
        url.Should().Be("/blazor/login/verify?id=emlog_01JMVAW4T4320KJ3A7EJMCG8R0&email=a%2Bb%40example.com&sent=1800000000&validFor=300&returnPath=%2Fblazor%2Fapp&resent=true");
    }
}
