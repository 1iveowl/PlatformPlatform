using Blazor.Host.Components.Pages.Public;
using FluentAssertions;
using SharedKernel.Domain;
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

    [Theory]
    [InlineData(ExternalLoginProvider.Google)]
    [InlineData(ExternalLoginProvider.Entra)]
    [InlineData(ExternalLoginProvider.MitId)]
    public void CreateForm_WhenLoginHasAPreferredTenant_ShouldCarryItToTheStartEndpoint(ExternalLoginProvider provider)
    {
        // Act
        var form = ExternalLoginStart.CreateForm(provider, ExternalLoginFlow.Login, "en-US", null, new TenantId(42));

        // Assert
        form.Fields.Should().ContainSingle(field => field.Key == "PreferredTenantId").Which.Value.Should().Be("42");
        form.Url.Should().EndWith("&ReturnPath=%2Fblazor%2Fapp&PreferredTenantId=42");
    }

    [Fact]
    public void CreateForm_WhenSignupHasAPreferredTenant_ShouldNotCarryIt()
    {
        // Act
        var form = ExternalLoginStart.CreateForm(ExternalLoginProvider.Google, ExternalLoginFlow.Signup, "en-US", null, new TenantId(42));

        // Assert
        form.Fields.Should().NotContain(field => field.Key == "PreferredTenantId");
    }

    [Fact]
    public void Create_WhenNoPreferredTenant_ShouldNotCarryThePreferredTenantParameter()
    {
        // Act
        var forms = ExternalLoginStart.Create(CreateFlags(true, true, true), ExternalLoginFlow.Login, "en-US", null);

        // Assert
        forms.Should().OnlyContain(form => form.Fields.All(field => field.Key != "PreferredTenantId"));
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
