using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUlid;
using SharedKernel.Localization;

namespace Blazor.Tests.Account;

// The one-time password input of the static login verification page through the real host: the markup a browser without
// scripts receives, and the states the page renders for the account API's refusals. Part of HostSecurityTests so it shares
// the one host the fixture starts and the environment variables it sets.
public sealed partial class HostSecurityTests
{
    [Fact]
    public async Task LoginVerifyPage_ShouldRenderOneLabelledInputPostingTheFormFieldWithHiddenSlots()
    {
        // Arrange
        var path = CreateLoginVerifyPath();

        // Act
        using var response = await fixture.Client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        InputTagPattern().Matches(html).Should().ContainSingle();
        var input = InputTagPattern().Match(html).Value;
        input.Should().Contain("id=\"one-time-password\"").And.Contain("name=\"Input.OneTimePassword\"").And.Contain("maxlength=\"6\"");
        input.Should().Contain("autocomplete=\"one-time-code\"").And.Contain("aria-describedby=\"one-time-password-validation one-time-password-error\"");
        input.Should().NotContain("disabled").And.NotContain("aria-invalid");
        html.Should().Contain($"<label for=\"one-time-password\" class=\"one-time-password-label\">{EnglishText(nameof(AuthenticationStrings.LoginVerificationCode))}</label>");
        html.Should().Contain("<div class=\"one-time-password-slots\" aria-hidden=\"true\" hidden data-one-time-password-slots>");
        Regex.Matches(html, "data-one-time-password-slot>").Should().HaveCount(6);
        html.Should().Contain("id=\"one-time-password-error\"").And.Contain("id=\"one-time-password-validation\"");
        html.Should().Contain("aria-live=\"polite\" data-testid=\"code-expired\"></p>");
        html.Should().NotContain("data-verification-state=").And.NotContain("data-testid=\"form-error-title\"");
    }

    [Fact]
    public async Task LoginVerifyPost_WhenApiRefusesTheCode_ShouldSendItUpperCaseAndRenderTheWrongCodeStateWithTheApiMessage()
    {
        // Arrange
        var emailLoginId = $"emlog_{Ulid.NewUlid()}";

        // Act
        var html = await PostLoginVerifyAsync(emailLoginId, "wrongb");

        // Assert
        fixture.CompletedOneTimePasswords[emailLoginId].Should().Be("WRONGB");
        html.Should().Contain("data-verification-state=\"wrong-code\"");
        html.Should().Contain($"<p class=\"form-error-title\" data-testid=\"form-error-title\">{EnglishText(nameof(AuthenticationStrings.VerificationStateWrongCode))}</p>");
        html.Should().Contain($"<p data-testid=\"form-error-message\">{HtmlEncoder.Default.Encode(HostFixture.WrongCodeMessage)}</p>");
        var input = InputTagPattern().Match(html).Value;
        input.Should().Contain("aria-describedby=\"one-time-password-validation one-time-password-error\"").And.Contain("value=\"\"").And.NotContain("disabled");
        SubmitTagPattern().Match(html).Value.Should().NotContain("disabled");
    }

    [Fact]
    public async Task LoginVerifyPost_WhenApiRefusesTooManyAttempts_ShouldRenderTheLockedStateWithInputAndVerifyDisabled()
    {
        // Arrange
        var emailLoginId = $"emlog_{Ulid.NewUlid()}";

        // Act
        var html = await PostLoginVerifyAsync(emailLoginId, HostFixture.LockedOneTimePassword.ToLowerInvariant());

        // Assert
        html.Should().Contain("data-verification-state=\"locked\"");
        html.Should().Contain($"<p class=\"form-error-title\" data-testid=\"form-error-title\">{EnglishText(nameof(AuthenticationStrings.VerificationStateLocked))}</p>");
        html.Should().Contain($"<p data-testid=\"form-error-message\">{HtmlEncoder.Default.Encode(HostFixture.TooManyAttemptsMessage)}</p>");
        InputTagPattern().Match(html).Value.Should().Contain("disabled");
        SubmitTagPattern().Match(html).Value.Should().Contain("disabled");
    }

    private async Task<string> PostLoginVerifyAsync(string emailLoginId, string oneTimePassword)
    {
        var client = fixture.Client;
        var path = CreateLoginVerifyPath(emailLoginId);
        var (cookie, formToken) = await fixture.GetFormAsync(client, path, null);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "login-verify",
                ["__RequestVerificationToken"] = formToken,
                ["Input.OneTimePassword"] = oneTimePassword
            }
        );
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Accept-Language", "en-US");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static string CreateLoginVerifyPath(string? emailLoginId = null)
    {
        var sent = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"blazor/login/verify?id={emailLoginId ?? $"emlog_{Ulid.NewUlid()}"}&email=ada%40example.com&sent={sent}&validFor=300";
    }

    private static string EnglishText(string key)
    {
        return HtmlEncoder.Default.Encode(AuthenticationStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en-US"))!);
    }

    [GeneratedRegex("<input[^>]*data-testid=\"code\"[^>]*>")]
    private static partial Regex InputTagPattern();

    [GeneratedRegex("<button[^>]*data-testid=\"submit\"[^>]*>")]
    private static partial Regex SubmitTagPattern();
}
