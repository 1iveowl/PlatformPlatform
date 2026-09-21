using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Blazor.Client.Development;
using Blazor.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Localization;

namespace Blazor.Tests.Account;

// The shared form error mapper and the static form-level error region through the real host: the Development-only fixture
// form is posted with its antiforgery token in a Danish UI culture, and the response HTML carries the messages. Part of
// HostSecurityTests so it shares the one host the fixture starts and the environment variables it sets.
public sealed partial class HostSecurityTests
{
    private const string StaticFixturePath = "blazor/development/form-errors/static";
    private const string InteractiveFixturePath = "blazor/development/form-errors/interactive";
    private const string DataListFixturePath = "blazor/development/data-list";

    [Fact]
    public async Task StaticFixturePost_WhenFieldsHaveSeveralMessages_ShouldRenderEveryMessageAtItsFieldAsEnglishText()
    {
        // Act
        var html = await PostStaticFixtureAsync(FormErrorScenarios.FieldMessages);

        // Assert
        html.Should().Contain("<html lang=\"da-DK\"");
        html.Should().Contain(FieldMessage(FormErrorScenarios.NameTooShortMessage));
        html.Should().Contain(FieldMessage(FormErrorScenarios.NameReservedMessage));
        html.Should().Contain(FieldMessage("Email &lt;b&gt;is&lt;/b&gt; already in use."));
        html.Should().NotContain("<b>is</b>");
        html.Should().NotContain("data-testid=\"form-error-message\"");
    }

    [Fact]
    public async Task StaticFixturePost_WhenKeyDiffersInCase_ShouldRenderMessageAtMatchingField()
    {
        // Act
        var html = await PostStaticFixtureAsync(FormErrorScenarios.CaseInsensitiveKey);

        // Assert
        html.Should().Contain(FieldMessage("Email &lt;b&gt;is&lt;/b&gt; already in use."));
        html.Should().NotContain("data-testid=\"form-error-message\"");
    }

    [Fact]
    public async Task StaticFixturePost_WhenKeyMatchesNoField_ShouldRenderFormLevelMessageInAlert()
    {
        // Act
        var html = await PostStaticFixtureAsync(FormErrorScenarios.UnmatchedKey);

        // Assert
        html.Should().Contain("role=\"alert\"");
        html.Should().Contain(FormMessage(FormErrorScenarios.TenantLockedMessage));
        html.Should().Contain(FieldMessage(FormErrorScenarios.NameReservedMessage));
    }

    [Theory]
    [InlineData(FormErrorScenarios.Detail, FormErrorScenarios.ConflictDetail)]
    [InlineData(FormErrorScenarios.TitleFallback, FormErrorScenarios.ConflictTitle)]
    [InlineData(FormErrorScenarios.TransportFailure, "Serveren kunne ikke nås. Tjek din forbindelse, og prøv igen.")]
    public async Task StaticFixturePost_WhenFailureIsNotValidation_ShouldRenderDetailThenTitleAsFormMessage(string scenario, string expectedMessage)
    {
        // Act
        var html = await PostStaticFixtureAsync(scenario);

        // Assert
        html.Should().Contain(FormMessage(expectedMessage));
        html.Should().NotContain("data-testid=\"form-error-reload\"");
    }

    [Fact]
    public async Task StaticFixturePost_WhenAntiforgeryTokenIsRejected_ShouldRenderRecoveryLinkToReloadThePage()
    {
        // Act
        var html = await PostStaticFixtureAsync(FormErrorScenarios.Antiforgery);

        // Assert
        html.Should().Contain(FormMessage(DanishText(nameof(CommonStrings.AntiforgeryRecovery))));
        html.Should().NotContain("The antiforgery token was not accepted.");
        html.Should().Contain($"<a href=\"/{StaticFixturePath}\" data-enhance-nav=\"false\" data-testid=\"form-error-reload\">{HtmlEncoder.Default.Encode(DanishText(nameof(CommonStrings.ReloadPage)))}</a>");
    }

    [Fact]
    public async Task StaticFixturePost_WhenUnauthorized_ShouldRenderNoMessage()
    {
        // Act
        var html = await PostStaticFixtureAsync(FormErrorScenarios.Unauthorized);

        // Assert
        html.Should().Contain("data-testid=\"applied-scenario\">unauthorized: Suppressed<");
        html.Should().NotContain(FormErrorScenarios.UnauthorizedDetail);
        html.Should().NotContain("data-testid=\"form-error-message\"");
    }

    [Fact]
    public async Task StaticFixturePost_WhenDataAnnotationsFail_ShouldRenderClientRuleMessageWithoutApplyingScenario()
    {
        // Act
        var html = await PostStaticFixtureAsync(FormErrorScenarios.Detail, "");

        // Assert
        html.Should().Contain(FieldMessage("The Name field is required."));
        html.Should().Contain("data-testid=\"applied-scenario\"><");
        html.Should().NotContain(FormErrorScenarios.ConflictDetail);
    }

    [Fact]
    public async Task StaticFixturePage_ShouldStartNoWebAssemblyRuntime()
    {
        // Act
        using var response = await fixture.Client.GetAsync(StaticFixturePath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // The import map is on every page; only preload links or a WebAssembly component marker would start the runtime
        html.Should().NotContain("rel=\"modulepreload\"").And.NotContain("<!--Blazor:{");
    }

    [Theory]
    [InlineData(StaticFixturePath)]
    [InlineData(InteractiveFixturePath)]
    [InlineData(DataListFixturePath)]
    [InlineData("blazor/development/throw")]
    [InlineData("blazor/development/tooltip")]
    public async Task FixturePages_WhenHostIsNotDevelopment_ShouldReturnNotFoundWithoutFixtureMarkup(string path)
    {
        // Arrange
        await using var productionHost = HostApplication.Build(["--environment", "Production", "--urls", "http://127.0.0.1:0"], fixture.TokenSigningClient);
        await productionHost.StartAsync();
        using var client = CreateHostClient(productionHost);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("fixture@example.com"));

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("Form errors").And.NotContain("Data list").And.NotContain("Something went wrong").And.NotContain("role=\"tooltip\"").And.NotContain("data-testid=\"scenario\"").And.NotContain("fixture-interactive");
    }

    [Fact]
    public async Task DataListFixturePage_WhenHostIsDevelopment_ShouldHostTheWebAssemblyFixture()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, DataListFixturePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("fixture@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("<h1>Data list</h1>").And.Contain("Blazor.Client.Development.DataListFixture");
    }

    [Theory]
    [InlineData(StaticFixturePath)]
    [InlineData(InteractiveFixturePath)]
    public async Task FixturePages_WhenHostIsDevelopment_ShouldBeReachable(string path)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("fixture@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("data-testid=\"scenario\"");
    }

    [Fact]
    public async Task StaticFixturePost_WhenTheAccountApiRefusedAField_ShouldMarkItInvalidAndDescribeItWithItsMessagesAndTheAlert()
    {
        // Act
        var html = await PostStaticFixtureAsync(FormErrorScenarios.FieldMessages);

        // Assert
        FieldTag(html, "name").Should().Contain("aria-invalid=\"true\"").And.Contain("aria-describedby=\"name-validation static-form-error\"");
        FieldTag(html, "email").Should().Contain("aria-invalid=\"true\"").And.Contain("aria-describedby=\"email-validation static-form-error\"");
        FieldTag(html, "scenario").Should().NotContain("aria-invalid").And.Contain("aria-describedby=\"scenario-validation static-form-error\"");
        html.Should().Contain("id=\"name-validation\"").And.Contain("id=\"email-validation\"").And.Contain("id=\"static-form-error\"");
    }

    [Fact]
    public async Task StaticFixturePage_WhenNothingIsRefused_ShouldDescribeEveryFieldWithoutMarkingItInvalid()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, StaticFixturePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("fixture@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        FieldTag(html, "name").Should().Contain("aria-describedby=\"name-validation static-form-error\"").And.NotContain("aria-invalid");
        html.Should().Contain("id=\"name-validation\"").And.Contain("id=\"static-form-error\"");
    }

    // The rendered tag of one field, so an assertion names the field it is about whatever order the attributes take
    private static string FieldTag(string html, string testId)
    {
        var match = Regex.Match(html, $"<(?:input|select)[^>]*data-testid=\"{testId}\"[^>]*>");
        match.Success.Should().BeTrue($"the {testId} field is rendered");
        return match.Value;
    }

    private async Task<string> PostStaticFixtureAsync(string scenario, string name = "Ada")
    {
        var client = fixture.Client;
        var (cookie, formToken) = await fixture.GetFormAsync(client, StaticFixturePath, null);
        using var request = new HttpRequestMessage(HttpMethod.Post, StaticFixturePath);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "form-errors-static",
                ["__RequestVerificationToken"] = formToken,
                ["Input.Name"] = name,
                ["Input.Email"] = "ada@example.com",
                ["Input.Scenario"] = scenario
            }
        );
        request.Headers.Add("Cookie", cookie);
        request.Headers.AcceptLanguage.ParseAdd("da-DK");

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static string FieldMessage(string encodedMessage)
    {
        return $"data-testid=\"{(encodedMessage.StartsWith("Email", StringComparison.Ordinal) ? "email" : "name")}-messages\">{encodedMessage}</div>";
    }

    private static string FormMessage(string message)
    {
        return $"<p data-testid=\"form-error-message\">{HtmlEncoder.Default.Encode(message)}</p>";
    }

    private static HttpClient CreateHostClient(WebApplication host)
    {
        var address = host.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", HostFixture.PublicHost);
        return client;
    }

    // The fixture posts in a Danish UI culture, so the page's own texts are the da-DK resources while API messages stay English
    private static string DanishText(string key)
    {
        return CommonStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("da-DK"))!;
    }
}
