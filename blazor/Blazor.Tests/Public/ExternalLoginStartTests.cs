using Blazor.Host.Components.Pages.Public;
using FluentAssertions;
using SharedKernel.FeatureFlags;

namespace Blazor.Tests.Public;

public sealed class ExternalLoginStartTests
{
    [Theory]
    [InlineData(false, false, false, ExternalLoginFlow.Login, "")]
    [InlineData(true, false, false, ExternalLoginFlow.Login, "Google")]
    [InlineData(false, true, false, ExternalLoginFlow.Login, "Entra")]
    [InlineData(false, false, true, ExternalLoginFlow.Login, "MitId")]
    [InlineData(true, true, true, ExternalLoginFlow.Login, "Google,Entra,MitId")]
    [InlineData(true, true, true, ExternalLoginFlow.Signup, "Google,Entra")]
    [InlineData(false, false, true, ExternalLoginFlow.Signup, "")]
    public void GetProviders_WhenFlagsGiven_ShouldOfferEnabledProvidersAndMitIdOnLoginOnly(bool google, bool entra, bool mitIdLogin, ExternalLoginFlow flow, string expected)
    {
        // Arrange
        var flags = CreateFlags(google, entra, mitIdLogin);

        // Act
        var providers = ExternalLoginStart.GetProviders(flags, flow);

        // Assert
        string.Join(',', providers).Should().Be(expected);
    }

    [Fact]
    public void GetProviders_WhenOnlyMitIdVerificationIsOn_ShouldNotOfferMitIdLogin()
    {
        // Arrange
        var flags = new Dictionary<string, bool> { [FeatureFlags.MitIdVerification.Key] = true };

        // Act
        var providers = ExternalLoginStart.GetProviders(flags, ExternalLoginFlow.Login);

        // Assert
        providers.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExternalLoginProvider.Google, ExternalLoginFlow.Login, "/api/account/authentication/Google/login/start")]
    [InlineData(ExternalLoginProvider.Entra, ExternalLoginFlow.Signup, "/api/account/authentication/Entra/signup/start")]
    [InlineData(ExternalLoginProvider.MitId, ExternalLoginFlow.Login, "/api/account/authentication/MitId/login/start")]
    public void CreateForm_WhenProviderAndFlowGiven_ShouldStartAtTheAccountApiForThisEdition(ExternalLoginProvider provider, ExternalLoginFlow flow, string action)
    {
        // Act
        var form = ExternalLoginStart.CreateForm(provider, flow, "da-DK", "/blazor/app/details?tab=1");

        // Assert
        form.Action.Should().Be(action);
        form.Fields.Should().Equal(
            new KeyValuePair<string, string>("Edition", "Blazor"),
            new KeyValuePair<string, string>("Locale", "da-DK"),
            new KeyValuePair<string, string>("ReturnPath", "/blazor/app/details?tab=1")
        );
        form.Url.Should().Be($"{action}?Edition=Blazor&Locale=da-DK&ReturnPath=%2Fblazor%2Fapp%2Fdetails%3Ftab%3D1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/dashboard")]
    [InlineData("/blazor/../dashboard")]
    [InlineData("/blazor/%2e%2e/dashboard")]
    [InlineData("//evil.example/blazor/app")]
    [InlineData("https://evil.example/blazor/app")]
    [InlineData("/blazor\\..\\dashboard")]
    [InlineData("/blazorx/app")]
    public void CreateForm_WhenReturnPathIsMissingOrOutsideThePathBase_ShouldReturnToTheAuthenticatedHome(string? returnPath)
    {
        // Act
        var form = ExternalLoginStart.CreateForm(ExternalLoginProvider.Google, ExternalLoginFlow.Login, "en-US", returnPath);

        // Assert
        form.Fields.Single(field => field.Key == "ReturnPath").Value.Should().Be("/blazor/app");
    }

    [Fact]
    public void Create_WhenFlagsAreOn_ShouldBuildOneFormPerProviderInOrder()
    {
        // Arrange
        var flags = CreateFlags(true, true, true);

        // Act
        var forms = ExternalLoginStart.Create(flags, ExternalLoginFlow.Login, "en-US", null);

        // Assert
        forms.Select(form => form.Provider).Should().Equal(ExternalLoginProvider.Google, ExternalLoginProvider.Entra, ExternalLoginProvider.MitId);
        forms.Should().OnlyContain(form => form.Url.EndsWith("?Edition=Blazor&Locale=en-US&ReturnPath=%2Fblazor%2Fapp", StringComparison.Ordinal));
    }

    private static Dictionary<string, bool> CreateFlags(bool google, bool entra, bool mitIdLogin)
    {
        return new Dictionary<string, bool>
        {
            [FeatureFlags.GoogleOauth.Key] = google,
            [FeatureFlags.EntraOauth.Key] = entra,
            [FeatureFlags.MitIdLogin.Key] = mitIdLogin,
            [FeatureFlags.MitIdVerification.Key] = true
        };
    }
}
