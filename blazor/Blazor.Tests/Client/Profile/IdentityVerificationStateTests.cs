using System.Globalization;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Queries;
using Blazor.Client.Profile;
using FluentAssertions;

namespace Blazor.Tests.Client.Profile;

public sealed class IdentityVerificationStateTests
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 9, 17, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void GetView_WhenVerificationFlagIsOff_ShouldBeHiddenWhateverTheStatus()
    {
        // Arrange
        var state = new IdentityVerificationState();
        state.Loaded(Verified(ExternalProviderType.MitId));

        // Act
        var view = state.GetView(false);

        // Assert
        view.Should().Be(IdentityVerificationView.Hidden);
        state.ShouldLoad(false).Should().BeFalse();
    }

    [Fact]
    public void GetView_WhenFlagIsOnAndStatusNotRead_ShouldBeLoadingAndAskForTheStatus()
    {
        // Arrange
        var state = new IdentityVerificationState();

        // Act
        var view = state.GetView(true);

        // Assert
        view.Should().Be(IdentityVerificationView.Loading);
        state.ShouldLoad(true).Should().BeTrue();
    }

    [Fact]
    public void GetView_WhenVerifiedWithMitId_ShouldBeVerifiedWithTheStatusAndNotLoadAgain()
    {
        // Arrange
        var state = new IdentityVerificationState();
        var status = Verified(ExternalProviderType.MitId);

        // Act
        state.Loaded(status);

        // Assert
        state.GetView(true).Should().Be(IdentityVerificationView.Verified);
        state.MitIdVerification.Should().Be(status);
        state.ShouldLoad(true).Should().BeFalse();
        state.TryBeginStart(true).Should().BeFalse();
    }

    [Fact]
    public void GetView_WhenNotVerified_ShouldBeUnverified()
    {
        // Arrange
        var state = new IdentityVerificationState();

        // Act
        state.Loaded(new VerificationStatusResponse(false, null, null, null, null));

        // Assert
        state.GetView(true).Should().Be(IdentityVerificationView.Unverified);
        state.MitIdVerification.Should().BeNull();
    }

    [Fact]
    public void GetView_WhenVerifiedWithAnotherProvider_ShouldOfferMitIdVerification()
    {
        // Arrange
        var state = new IdentityVerificationState();

        // Act
        state.Loaded(Verified(ExternalProviderType.Entra));

        // Assert
        state.GetView(true).Should().Be(IdentityVerificationView.Unverified);
        state.MitIdVerification.Should().BeNull();
    }

    [Fact]
    public void GetView_WhenStatusCouldNotBeRead_ShouldBeUnavailableAndRefuseAStart()
    {
        // Arrange
        var state = new IdentityVerificationState();

        // Act
        state.LoadFailed();

        // Assert
        state.GetView(true).Should().Be(IdentityVerificationView.Unavailable);
        state.TryBeginStart(true).Should().BeFalse();
        state.ShouldLoad(true).Should().BeFalse();
    }

    [Fact]
    public void TryBeginStart_WhenUnverified_ShouldBeRedirectingAndRefuseASecondStart()
    {
        // Arrange
        var state = new IdentityVerificationState();
        state.Loaded(new VerificationStatusResponse(false, null, null, null, null));

        // Act
        var first = state.TryBeginStart(true);
        var second = state.TryBeginStart(true);

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
        state.GetView(true).Should().Be(IdentityVerificationView.Redirecting);
    }

    [Fact]
    public void TryBeginStart_WhenStatusIsStillLoading_ShouldRefuseTheStart()
    {
        // Arrange
        var state = new IdentityVerificationState();

        // Act
        var started = state.TryBeginStart(true);

        // Assert
        started.Should().BeFalse();
        state.GetView(true).Should().Be(IdentityVerificationView.Loading);
    }

    [Fact]
    public void StartFailed_WhenRedirecting_ShouldOfferTheButtonAgainForAnExplicitRetry()
    {
        // Arrange
        var state = new IdentityVerificationState();
        state.Loaded(new VerificationStatusResponse(false, null, null, null, null));
        state.TryBeginStart(true);

        // Act
        state.StartFailed();

        // Assert
        state.GetView(true).Should().Be(IdentityVerificationView.Unverified);
        state.TryBeginStart(true).Should().BeTrue();
    }

    [Fact]
    public void ReturnPath_WhenRead_ShouldBeTheProfileBelowThePathBase()
    {
        // Act
        var returnPath = IdentityVerificationState.ReturnPath;

        // Assert
        returnPath.Should().Be("/blazor/user/profile");
    }

    [Theory]
    [InlineData("https://broker.example/authorize?state=abc", true)]
    [InlineData("https://app.dev.localhost:9000/api/account/authentication/MitId/verification/callback?code=x", true)]
    [InlineData("http://localhost:9000/authorize", true)]
    [InlineData("/api/account/authentication/MitId/verification/callback", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("//evil.example/authorize", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsNavigableAuthorizationUrl_WhenUrlGiven_ShouldAcceptOnlyAbsoluteHttpUrls(string? authorizationUrl, bool expected)
    {
        // Act
        var navigable = IdentityVerificationState.IsNavigableAuthorizationUrl(authorizationUrl);

        // Assert
        navigable.Should().Be(expected);
    }

    [Theory]
    [InlineData(IdentityAssuranceLevel.Low, "Low assurance", "Lavt sikringsniveau")]
    [InlineData(IdentityAssuranceLevel.Substantial, "Substantial assurance", "Betydeligt sikringsniveau")]
    [InlineData(IdentityAssuranceLevel.High, "High assurance", "Højt sikringsniveau")]
    [InlineData(null, "Unknown assurance", "Ukendt sikringsniveau")]
    public void GetAssuranceLevelLabel_WhenLevelGiven_ShouldReturnTheLabelInBothCultures(IdentityAssuranceLevel? assuranceLevel, string english, string danish)
    {
        // Act
        var labels = InCultures(() => IdentityVerificationState.GetAssuranceLevelLabel(assuranceLevel));

        // Assert
        labels.Should().Equal(english, danish);
    }

    [Fact]
    public void FormatVerifiedDate_WhenDateGiven_ShouldUseTheCultureShortDateFormat()
    {
        // Act
        var formatted = InCultures(() => IdentityVerificationState.FormatVerifiedDate(VerifiedAt));

        // Assert
        var local = VerifiedAt.ToLocalTime();
        formatted.Should().Equal(local.ToString("d", new CultureInfo("en-US")), local.ToString("d", new CultureInfo("da-DK")));
        IdentityVerificationState.FormatVerifiedDate(null).Should().BeEmpty();
    }

    private static VerificationStatusResponse Verified(ExternalProviderType provider)
    {
        return new VerificationStatusResponse(true, provider, IdentityAssuranceLevel.Substantial, VerifiedAt, VerifiedAt);
    }

    private static string[] InCultures(Func<string> read)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            return new[] { "en-US", "da-DK" }.Select(name =>
                {
                    CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
                    return read();
                }
            ).ToArray();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
