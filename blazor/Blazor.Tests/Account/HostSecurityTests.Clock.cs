using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUlid;
using SharedKernel.Localization;

namespace Blazor.Tests.Account;

// The static verification pages read the current time from the host's registered clock: a host started with a fixed clock
// far from the real time shows the remaining validity and the resend action for that clock, and the start and resend
// handlers write that clock's instant into the verification state they redirect with. Part of HostSecurityTests so it
// shares the stand-in account API the fixture starts.
public sealed partial class HostSecurityTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("login/verify", 0, 300, 30)]
    [InlineData("login/verify", 29, 271, 1)]
    [InlineData("login/verify", 30, 270, 0)]
    [InlineData("login/verify", 299, 1, 0)]
    [InlineData("login/verify", 300, 0, 0)]
    [InlineData("login/verify", -60, 300, 30)]
    [InlineData("signup/verify", 29, 271, 1)]
    [InlineData("signup/verify", 30, 270, 0)]
    [InlineData("signup/verify", 300, 0, 0)]
    public async Task VerifyPage_WhenHostClockIsFixed_ShouldShowValidityAndResendForThatClock(string route, int secondsSinceSent, int expectedRemaining, int expectedResendIn)
    {
        // Arrange
        await using var host = await fixture.StartAdditionalHostAsync(new FixedTimeProvider(FixedNow));
        var sent = FixedNow.AddSeconds(-secondsSinceSent).ToUnixTimeSeconds();

        // Act
        using var response = await host.Client.GetAsync(CreateVerifyPath(route, sent));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"data-remaining-seconds=\"{expectedRemaining}\" data-resend-in-seconds=\"{expectedResendIn}\"");
        ResendRevealPattern().Match(html).Groups["hidden"].Success.Should().Be(expectedResendIn > 0);
        var expiredText = expectedRemaining == 0 ? EnglishText(nameof(AuthenticationStrings.VerificationCodeExpired)) : "";
        html.Should().Contain($"data-testid=\"code-expired\">{expiredText}</p>");
    }

    [Theory]
    [InlineData("login", "login-start", "login/verify")]
    [InlineData("signup", "signup-start", "signup/verify")]
    public async Task StartPost_WhenHostClockIsFixed_ShouldRedirectWithTheClockAsTheSentTime(string route, string handler, string verifyRoute)
    {
        // Arrange
        await using var host = await fixture.StartAdditionalHostAsync(new FixedTimeProvider(FixedNow));
        var (cookie, formToken) = await fixture.GetFormAsync(host.Client, $"blazor/{route}", null);
        using var request = CreateFormPost($"blazor/{route}", cookie, formToken, handler, ("Input.Email", $"clock-{Guid.NewGuid():N}@example.com"));

        // Act
        using var response = await host.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.OriginalString;
        location.Should().StartWith($"https://{HostFixture.PublicHost}/blazor/{verifyRoute}?id=");
        location.Should().Contain($"&sent={FixedNow.ToUnixTimeSeconds()}&validFor=300");
    }

    [Theory]
    [InlineData("login/verify", "login-resend")]
    [InlineData("signup/verify", "signup-resend")]
    public async Task ResendPost_WhenHostClockHasMoved_ShouldRedirectWithTheCurrentClockAsTheSentTime(string route, string handler)
    {
        // Arrange
        var clock = new FixedTimeProvider(FixedNow);
        await using var host = await fixture.StartAdditionalHostAsync(clock);
        var path = CreateVerifyPath(route, FixedNow.AddSeconds(-40).ToUnixTimeSeconds());
        var (cookie, formToken) = await fixture.GetFormAsync(host.Client, path, null);
        clock.Now = FixedNow.AddSeconds(20);
        using var request = CreateFormPost(path, cookie, formToken, handler);

        // Act
        using var response = await host.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.OriginalString;
        location.Should().StartWith($"https://{HostFixture.PublicHost}/blazor/{route}?id=");
        location.Should().Contain($"&sent={clock.Now.ToUnixTimeSeconds()}&validFor=300").And.EndWith("&resent=true");
    }

    private static string CreateVerifyPath(string route, long sent)
    {
        return string.Create(CultureInfo.InvariantCulture, $"blazor/{route}?id=emlog_{Ulid.NewUlid()}&email=ada%40example.com&sent={sent}&validFor=300");
    }

    private static HttpRequestMessage CreateFormPost(string path, string cookie, string formToken, string handler, params (string Name, string Value)[] fields)
    {
        var values = new Dictionary<string, string> { ["_handler"] = handler, ["__RequestVerificationToken"] = formToken };
        foreach (var (name, value) in fields)
        {
            values[name] = value;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(values) };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Accept-Language", "en-US");
        return request;
    }

    [GeneratedRegex("<div data-resend-reveal data-testid=\"resend\"(?<hidden> hidden)?>")]
    private static partial Regex ResendRevealPattern();
}
