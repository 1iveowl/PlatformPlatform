using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The external provider buttons on the static login and signup pages and the error page a refused external login or signup
// lands on, through the real host. The buttons follow the system feature flags the host reads from its environment, so the
// tests set those variables for the duration of one request; the host collection runs its tests one at a time. Part of
// HostSecurityTests so it shares the one host the fixture starts.
public sealed partial class HostSecurityTests
{
    private const string GoogleFlagVariable = "PUBLIC_GOOGLE_OAUTH_ENABLED";
    private const string EntraFlagVariable = "PUBLIC_ENTRA_OAUTH_ENABLED";
    private const string MitIdLoginFlagVariable = "PUBLIC_MITID_LOGIN_ENABLED";
    private const string MitIdVerificationFlagVariable = "PUBLIC_MITID_VERIFICATION_ENABLED";

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task LoginPage_WhenProviderFlagsVary_ShouldRenderExactlyTheEnabledProviders(bool google, bool entra, bool mitIdLogin)
    {
        // Act
        var html = await GetWithProviderFlagsAsync("blazor/login", "en-US", google, entra, mitIdLogin);

        // Assert
        html.Contains("data-testid=\"external-login-options\"").Should().Be(google || entra || mitIdLogin);
        html.Contains(">or</span>").Should().Be(google || entra || mitIdLogin);
        html.Contains("data-testid=\"external-login-google\"").Should().Be(google);
        html.Contains("Log in with Google").Should().Be(google);
        html.Contains("data-testid=\"external-login-entra\"").Should().Be(entra);
        html.Contains("Log in with Microsoft").Should().Be(entra);
        html.Contains("data-testid=\"external-login-mitid\"").Should().Be(mitIdLogin);
        html.Should().NotContain("Log in with MitID");
    }

    [Fact]
    public async Task LoginPage_WhenOnlyMitIdVerificationIsOn_ShouldNotOfferMitIdLogin()
    {
        // Arrange
        using var flags = new ProviderFlags(false, false, false, true);

        // Act
        var html = await GetPageAsync("blazor/login", "en-US");

        // Assert
        html.Should().NotContain("data-testid=\"external-login-options\"").And.NotContain("mitid-logo");
    }

    [Theory]
    [InlineData("en-US", "Sign up with Google", "Sign up with Microsoft", "eller")]
    [InlineData("da-DK", "Tilmeld dig med Google", "Tilmeld dig med Microsoft", ">or<")]
    public async Task SignupPage_WhenEveryProviderFlagIsOn_ShouldOfferGoogleAndEntraSignupButNeverMitId(string culture, string google, string entra, string otherCultureDivider)
    {
        // Act
        var html = await GetWithProviderFlagsAsync("blazor/signup", culture, true, true, true);

        // Assert
        html.Should().Contain(google).And.Contain(entra);
        html.Should().Contain("action=\"/api/account/authentication/Google/signup/start\"").And.Contain("action=\"/api/account/authentication/Entra/signup/start\"");
        html.Should().NotContain("data-testid=\"external-login-mitid\"").And.NotContain("/MitId/").And.NotContain("mitid-logo");
        html.Should().NotContain(otherCultureDivider);
        FindStartFields(html, "Google").Should().Equal(("Edition", "Blazor"), ("Locale", culture), ("ReturnPath", "/blazor/app"));
    }

    [Theory]
    [InlineData("en-US", "Log on with", "/blazor/app/details?tab=1", "/blazor/app/details?tab=1")]
    [InlineData("da-DK", "Log ind med", "/blazor/users", "/blazor/users")]
    [InlineData("en-US", "Log on with", "/blazor/../dashboard", "/blazor/app")]
    [InlineData("en-US", "Log on with", "//evil.example/blazor/app", "/blazor/app")]
    [InlineData("en-US", "Log on with", "https://evil.example/blazor/app", "/blazor/app")]
    [InlineData("en-US", "Log on with", "/dashboard", "/blazor/app")]
    public async Task LoginPage_WhenReturnPathGiven_ShouldStartEveryProviderForThisEditionWithAReturnPathUnderThePathBase(string culture, string logOnWith, string returnPath, string expectedReturnPath)
    {
        // Act
        var html = await GetWithProviderFlagsAsync($"blazor/login?returnPath={Uri.EscapeDataString(returnPath)}", culture, true, true, true);

        // Assert
        foreach (var provider in new[] { "Google", "Entra", "MitId" })
        {
            html.Should().Contain($"action=\"/api/account/authentication/{provider}/login/start\"");
            FindStartFields(html, provider).Should().Equal(("Edition", "Blazor"), ("Locale", culture), ("ReturnPath", expectedReturnPath));
        }

        html.Should().MatchRegex($"<button type=\"submit\" class=\"mitid-button\">\\s*{logOnWith} <img src=\"/blazor/images/mitid-logo-white[^\"]*\\.svg\" alt=\"MitID\" class=\"mitid-wordmark\"");
        html.Should().MatchRegex("<img src=\"/blazor/images/google-icon[^\"]*\\.svg\" alt aria-hidden=\"true\"");
        ExternalLoginSection(html).Should().NotContain("style=");
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("0", null)]
    [InlineData("-7", null)]
    [InlineData("not-a-tenant", null)]
    public async Task LoginPage_WhenPreferredTenantCookieGiven_ShouldCarryOnlyAValidTenantToEveryLoginStartAndNeverToSignup(string cookieValue, string? expectedTenantId)
    {
        // Arrange
        using var flags = new ProviderFlags(true, true, true, true);

        // Act
        var loginHtml = await GetPageWithCookieAsync("blazor/login", $"preferred-tenant={cookieValue}");
        var signupHtml = await GetPageWithCookieAsync("blazor/signup", $"preferred-tenant={cookieValue}");

        // Assert
        foreach (var provider in new[] { "Google", "Entra", "MitId" })
        {
            var preferredTenantFields = FindStartFields(loginHtml, provider).Where(field => field.Name == "PreferredTenantId").Select(field => field.Value).ToArray();
            preferredTenantFields.Should().Equal(expectedTenantId is null ? [] : [expectedTenantId]);
        }

        foreach (var provider in new[] { "Google", "Entra" })
        {
            FindStartFields(signupHtml, provider).Should().NotContain(field => field.Name == "PreferredTenantId");
        }
    }

    [Theory]
    [InlineData("user_not_found", "en-US", "Account not found", "No account found for this email address. Please sign up to create an account.", "signup,login")]
    [InlineData("user_not_found", "da-DK", "Konto ikke fundet", "Ingen konto fundet for denne e-mailadresse. Tilmeld dig venligst for at oprette en konto.", "signup,login")]
    [InlineData("account_already_exists", "en-US", "Account already exists", "An account with this email already exists. Please log in instead.", "login,signup")]
    [InlineData("account_already_exists", "da-DK", "Konto findes allerede", "Der findes allerede en konto med denne e-mailadresse. Log ind i stedet.", "login,signup")]
    [InlineData("email_not_provided", "en-US", "Email address required", "The identity provider did not share a verified email address, which is needed to create an account. Sign up with your email instead.", "signup,login")]
    [InlineData("email_not_provided", "da-DK", "E-mailadresse påkrævet", "Identitetsudbyderen delte ikke en bekræftet e-mailadresse, som er nødvendig for at oprette en konto. Tilmeld dig i stedet med din e-mail.", "signup,login")]
    [InlineData("authentication_failed", "en-US", "Authentication failed", "We detected a security issue with your login attempt. Please try again.", "login")]
    [InlineData("authentication_failed", "da-DK", "Godkendelse mislykkedes", "Vi opdagede et sikkerhedsproblem med dit loginforsøg. Prøv venligst igen.", "login")]
    [InlineData("invalid_request", "en-US", "Invalid request", "The authentication request was invalid. Please try again.", "login")]
    [InlineData("invalid_request", "da-DK", "Ugyldig anmodning", "Godkendelsesanmodningen var ugyldig. Prøv venligst igen.", "login")]
    [InlineData("access_denied", "en-US", "Access denied", "Authentication was cancelled or denied. Please try again if you want to continue.", "login")]
    [InlineData("access_denied", "da-DK", "Adgang nægtet", "Godkendelse blev annulleret eller afvist. Prøv venligst igen, hvis du vil fortsætte.", "login")]
    [InlineData("identity_not_verified", "en-US", "Identity not verified", "Log in another way, then verify your identity from your profile to sign in with MitID.", "login")]
    [InlineData("identity_not_verified", "da-DK", "Identitet ikke bekræftet", "Log ind på en anden måde, og bekræft derefter din identitet fra din profil for at kunne logge ind med MitID.", "login")]
    [InlineData("server_error", "en-US", "Something went wrong", "An unexpected error occurred. Please try again or contact support if the problem persists.", "login")]
    [InlineData("server_error", "da-DK", "Noget gik galt", "Der opstod en uventet fejl. Prøv igen eller kontakt support, hvis problemet fortsætter.", "login")]
    public async Task ErrorPage_WhenExternalAuthenticationCodeGiven_ShouldRenderItsTextActionsAndReferenceIdInTheCulture(string errorCode, string culture, string title, string message, string actions)
    {
        // Act
        var html = WebUtility.HtmlDecode(await GetPageAsync($"blazor/error?error={errorCode}&id=exlog_01JZ8Q4N6V3K2M7P9R5T0W1XYZ", culture));

        // Assert
        html.Should().Contain(PublicNavigationMarker).And.Contain($">{title}</h1>").And.Contain(message);
        html.Should().Contain($"data-error-code=\"{errorCode}\"");
        var referenceLabel = culture == "da-DK" ? "Reference-ID" : "Reference ID";
        html.Should().Contain($"data-testid=\"error-reference-id\">{referenceLabel}: exlog_01JZ8Q4N6V3K2M7P9R5T0W1XYZ</p>");
        ErrorActions(html).Should().Equal(actions.Split(',').Select(action => (action, $"/blazor/{action}")));
        html.Should().NotContain("data-testid=\"error-try-again\"");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "<img src=x onerror=alert(1)>")]
    [InlineData("identity_already_linked\" data-injected=\"1", "exlog_1\" data-injected=\"1")]
    [InlineData("access_denied&error_description=The user denied access", "exlog_1")]
    public async Task ErrorPage_WhenQueryValuesAreHostile_ShouldRenderGenericTextAndNeverEchoThem(string errorCode, string id)
    {
        // Act
        var html = await GetPageAsync($"blazor/error?error={Uri.EscapeDataString(errorCode)}&id={Uri.EscapeDataString(id)}&error_description={Uri.EscapeDataString("The user denied access")}", "en-US");

        // Assert
        WebUtility.HtmlDecode(html).Should().Contain(">Something went wrong</h1>");
        html.Should().NotContain("<script>alert").And.NotContain("<img src=x").And.NotContain("data-injected").And.NotContain("data-error-code");
        html.Should().NotContain("The user denied access").And.NotContain("error_description");
        WebUtility.HtmlDecode(html).Should().NotContain(errorCode).And.NotContain("Reference ID:");
    }

    [Fact]
    public async Task ErrorPage_WhenKnownCodeCarriesAMalformedId_ShouldOmitTheReferenceId()
    {
        // Act
        var html = WebUtility.HtmlDecode(await GetPageAsync($"blazor/error?error=access_denied&id={Uri.EscapeDataString("<b>exlog</b>")}", "en-US"));

        // Assert
        html.Should().Contain(">Access denied</h1>").And.NotContain("Reference ID:").And.NotContain("<b>exlog</b>");
    }

    private async Task<string> GetWithProviderFlagsAsync(string path, string culture, bool google, bool entra, bool mitIdLogin)
    {
        using var flags = new ProviderFlags(google, entra, mitIdLogin, true);
        return await GetPageAsync(path, culture);
    }

    private async Task<string> GetPageAsync(string path, string culture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));
        using var response = await fixture.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> GetPageWithCookieAsync(string path, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
        request.Headers.Add("Cookie", cookie);
        using var response = await fixture.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static string ExternalLoginSection(string html)
    {
        var start = html.IndexOf("data-testid=\"external-login-options\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        return html[start..html.IndexOf("data-testid=\"form-error", start, StringComparison.Ordinal)];
    }

    // The hidden inputs of the provider's start form, in document order, decoded
    private static (string Name, string Value)[] FindStartFields(string html, string provider)
    {
        var form = Regex.Match(html, $"<form method=\"get\" action=\"/api/account/authentication/{provider}/[a-z]+/start\"[^>]*>(.*?)</form>", RegexOptions.Singleline);
        form.Success.Should().BeTrue();
        return Regex.Matches(form.Groups[1].Value, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")
            .Select(match => (match.Groups[1].Value, WebUtility.HtmlDecode(match.Groups[2].Value)))
            .ToArray();
    }

    private static (string TestId, string Href)[] ErrorActions(string html)
    {
        return Regex.Matches(html, "<a href=\"([^\"]+)\" class=\"button-(?:primary|secondary)\" data-enhance-nav=\"false\" data-testid=\"error-(login|signup)\"")
            .Select(match => (match.Groups[2].Value, match.Groups[1].Value))
            .ToArray();
    }

    // Sets the provider flags for the host, which reads them per request, and restores the previous values on dispose
    private sealed class ProviderFlags : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public ProviderFlags(bool google, bool entra, bool mitIdLogin, bool mitIdVerification)
        {
            Set(GoogleFlagVariable, google);
            Set(EntraFlagVariable, entra);
            Set(MitIdLoginFlagVariable, mitIdLogin);
            Set(MitIdVerificationFlagVariable, mitIdVerification);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        private void Set(string name, bool enabled)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, enabled ? "true" : "false");
        }
    }
}
