using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The request culture through the real host: authentication runs first, so a valid locale claim decides even when the
// browser asks for the other language; an anonymous request follows Accept-Language with base-language fallback. The page
// text, the validation messages and <html lang> all use the chosen culture. Part of HostSecurityTests so it shares the one
// host the fixture starts.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("da-DK", "da-DK", "Hej! Velkommen tilbage")]
    [InlineData("da", "da-DK", "Hej! Velkommen tilbage")]
    [InlineData("fr-FR, da;q=0.8", "da-DK", "Hej! Velkommen tilbage")]
    [InlineData("en-GB", "en-US", "Hi! Welcome back")]
    [InlineData("fr-FR, de-DE;q=0.5", "en-US", "Hi! Welcome back")]
    public async Task PublicPage_WhenAnonymous_ShouldRenderInAcceptLanguageCulture(string acceptLanguage, string expectedLocale, string expectedHeading)
    {
        // Act
        var html = await GetLoginPageAsync(acceptLanguage, null);

        // Assert
        html.Should().Contain($"<html lang=\"{expectedLocale}\"");
        html.Should().Contain($"<h1>{expectedHeading}</h1>");
    }

    [Theory]
    [InlineData("en-US", "da-DK", "en-US", "Hi! Welcome back")]
    [InlineData("da-DK", "en-US", "da-DK", "Hej! Velkommen tilbage")]
    [InlineData("fr-FR", "da-DK", "en-US", "Hi! Welcome back")]
    public async Task PublicPage_WhenLocaleClaimConflictsWithAcceptLanguage_ShouldRenderInClaimCulture(string claimLocale, string acceptLanguage, string expectedLocale, string expectedHeading)
    {
        // Act
        var html = await GetLoginPageAsync(acceptLanguage, fixture.CreateToken("culture@example.com", locale: claimLocale));

        // Assert
        html.Should().Contain($"<html lang=\"{expectedLocale}\"");
        html.Should().Contain($"<h1>{expectedHeading}</h1>");
    }

    [Fact]
    public async Task PublicFormPost_WhenCultureIsDanish_ShouldRenderDanishValidationMessage()
    {
        // Arrange
        var client = fixture.Client;
        var (cookie, formToken) = await fixture.GetLoginFormAsync(client, null);
        using var request = HostFixture.CreateLoginPost("", cookie, formToken, null);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("da-DK"));

        // Act
        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        html.Should().Contain("<html lang=\"da-DK\"");
        html.Should().Contain($"data-valmsg-for=\"Input.Email\" data-valmsg-replace=\"true\">{HtmlEncoder.Default.Encode("E-mailadresse påkrævet")}</div>");
        html.Should().NotContain("The Email field is required.");
    }

    private async Task<string> GetLoginPageAsync(string acceptLanguage, string? bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/login");
        request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        if (bearerToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await fixture.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
