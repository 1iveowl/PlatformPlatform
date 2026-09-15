using System.Net;
using System.Net.Http.Headers;
using Blazor.Client.Development;
using Blazor.Client.Forms;
using Blazor.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Tests.Account;

// The shared form error mapper and the static form-level error region through the real host: the Development-only fixture
// form is posted with its antiforgery token in a Danish UI culture, and the response HTML carries the messages. Part of
// HostSecurityTests so it shares the one host the fixture starts and the environment variables it sets.
public sealed partial class HostSecurityTests
{
    private const string StaticFixturePath = "blazor/development/form-errors/static";
    private const string InteractiveFixturePath = "blazor/development/form-errors/interactive";

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
    [InlineData(FormErrorScenarios.TransportFailure, ApiFailureClassifier.TransportFailureMessage)]
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
        html.Should().Contain(FormMessage(ApiFailureClassifier.AntiforgeryRecoveryMessage));
        html.Should().NotContain("The antiforgery token was not accepted.");
        html.Should().Contain($"<a href=\"/{StaticFixturePath}\" data-enhance-nav=\"false\" data-testid=\"form-error-reload\">{ApiFailureClassifier.ReloadPageActionLabel}</a>");
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
        html.Should().NotContain("Form errors").And.NotContain("data-testid=\"scenario\"").And.NotContain("fixture-interactive");
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
        return $"<p data-testid=\"form-error-message\">{message}</p>";
    }

    private static HttpClient CreateHostClient(WebApplication host)
    {
        var address = host.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", HostFixture.PublicHost);
        return client;
    }
}
