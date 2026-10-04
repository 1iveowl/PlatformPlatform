using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The request culture through the real host, in the order claim, preferred-locale cookie, Accept-Language, default:
// authentication runs first, so a valid locale claim decides even when the cookie or the browser asks for the other
// language; an anonymous request follows a supported preferred-locale cookie, else Accept-Language with base-language
// fallback, and a malformed cookie is ignored. The page
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
    [InlineData("da-DK", "en-US", "da-DK", "Hej! Velkommen tilbage")]
    [InlineData("en-US", "da-DK", "en-US", "Hi! Welcome back")]
    [InlineData("da", "en-US", "en-US", "Hi! Welcome back")]
    [InlineData("fr-FR", "da-DK", "da-DK", "Hej! Velkommen tilbage")]
    [InlineData("%3Cscript%3E", "da-DK", "da-DK", "Hej! Velkommen tilbage")]
    [InlineData("", "fr-FR", "en-US", "Hi! Welcome back")]
    public async Task PublicPage_WhenAnonymousWithPreferredLocaleCookie_ShouldPreferASupportedCookieOverAcceptLanguage(string cookieValue, string acceptLanguage, string expectedLocale, string expectedHeading)
    {
        // Act
        var html = await GetLoginPageAsync(acceptLanguage, null, cookieValue);

        // Assert
        html.Should().Contain($"<html lang=\"{expectedLocale}\"");
        html.Should().Contain($"<h1>{expectedHeading}</h1>");
    }

    [Theory]
    [InlineData("en-US", "da-DK", "en-US", "Hi! Welcome back")]
    [InlineData("da-DK", "en-US", "da-DK", "Hej! Velkommen tilbage")]
    public async Task PublicPage_WhenLocaleClaimConflictsWithPreferredLocaleCookie_ShouldRenderInClaimCulture(string claimLocale, string cookieValue, string expectedLocale, string expectedHeading)
    {
        // Act
        var html = await GetLoginPageAsync(cookieValue, fixture.CreateToken("culture-cookie@example.com", locale: claimLocale), cookieValue);

        // Assert
        html.Should().Contain($"<html lang=\"{expectedLocale}\"");
        html.Should().Contain($"<h1>{expectedHeading}</h1>");
    }

    [Theory]
    [InlineData("en-US", "da-DK")]
    [InlineData("da-DK", "en-US")]
    public async Task PreferencesPage_WhenAuthenticated_ShouldPrerenderInClaimCultureWithTheCurrentLanguageSelected(string claimLocale, string cookieValue)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/user/preferences");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("preferences@example.com", locale: claimLocale));
        request.Headers.Add("Cookie", $"preferred-locale={cookieValue}");

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain($"<html lang=\"{claimLocale}\"");
        html.Should().MatchRegex($@"value=""{claimLocale}""[^>]*checked");
        html.Should().NotMatchRegex($@"value=""{cookieValue}""[^>]*checked");
        html.Should().NotContain("style=");
    }

    // The theme and zoom labels resolve in each request's culture, in either order and concurrently, on the one host the
    // fixture shares, so a culture that rendered first cannot leave its labels behind for the next request
    [Fact]
    public async Task PreferencesPage_WhenCulturesAlternateInOneHost_ShouldPrerenderThemeAndZoomLabelsInEachRequestCulture()
    {
        // Arrange
        string[] sequence = ["en-US", "da-DK", "en-US", "da-DK"];

        // Act
        var sequentialPages = new List<(string Locale, string Html)>();
        foreach (var locale in sequence)
        {
            sequentialPages.Add((locale, await GetPreferencesPageAsync(locale)));
        }

        var concurrentPages = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
                {
                    var locale = index % 2 == 0 ? "da-DK" : "en-US";
                    return (Locale: locale, Html: await GetPreferencesPageAsync(locale));
                }
            )
        );

        // Assert
        foreach (var (locale, html) in sequentialPages.Concat(concurrentPages))
        {
            AssertPreferenceLabels(html, locale);
        }
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

    private static void AssertPreferenceLabels(string html, string locale)
    {
        var danish = locale == "da-DK";
        (string TestId, string Value, string Label)[] expected =
        [
            ("theme-system", "system", "System"),
            ("theme-light", "light", danish ? "Lys" : "Light"),
            ("theme-dark", "dark", danish ? "Mørk" : "Dark"),
            ("zoom-level-0.875", "0.875", danish ? "Lille" : "Small"),
            ("zoom-level-1", "1", danish ? "Standard" : "Default"),
            ("zoom-level-1.125", "1.125", danish ? "Stor" : "Large"),
            ("zoom-level-1.25", "1.25", danish ? "Større" : "Larger")
        ];

        html.Should().Contain($"<html lang=\"{locale}\"");
        foreach (var (testId, value, label) in expected)
        {
            // Prerender renders the group disabled with no choice checked, because theme and zoom are only known in the browser
            var item = $@"data-testid=""{Regex.Escape(testId)}""[^>]*>\s*<input[^>]*value=""{Regex.Escape(value)}""[^>]*>\s*<span>{Regex.Escape(HtmlEncoder.Default.Encode(label))}</span>";
            html.Should().MatchRegex(item, $"the {testId} option of a {locale} page reads {label}");
            Regex.Match(html, item).Value.Should().Contain("disabled").And.NotContain("checked");
        }
    }

    private async Task<string> GetPreferencesPageAsync(string locale)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/user/preferences");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("preferences@example.com", locale: locale));
        using var response = await fixture.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> GetLoginPageAsync(string acceptLanguage, string? bearerToken, string? preferredLocaleCookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/login");
        request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        if (preferredLocaleCookie is not null) request.Headers.TryAddWithoutValidation("Cookie", $"preferred-locale={preferredLocaleCookie}");
        if (bearerToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await fixture.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
