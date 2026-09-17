using System.Net;
using System.Net.Http.Headers;
using Blazor.Host;
using Blazor.Host.Account;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazor.Tests.Account;

[Collection(HostCollection.Name)]
public sealed partial class HostSecurityTests(HostFixture fixture)
{
    public enum InvalidToken
    {
        WrongIssuer,
        WrongAudience,
        WrongKey,
        ExpiredBeyondClockSkew
    }

    [Fact]
    public async Task AuthenticatedPrerender_WhenTokenIsSignedByDevelopmentSigningClient_ShouldBeAuthenticated()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await GetAuthenticatedPageAsync(client, fixture.CreateToken("valid@example.com"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("data-testid=\"bootstrap-email\">valid@example.com<");
    }

    [Theory]
    [InlineData(InvalidToken.WrongIssuer)]
    [InlineData(InvalidToken.WrongAudience)]
    [InlineData(InvalidToken.WrongKey)]
    [InlineData(InvalidToken.ExpiredBeyondClockSkew)]
    public async Task AuthenticatedPrerender_WhenTokenIsInvalid_ShouldBeAnonymous(InvalidToken invalidToken)
    {
        // Arrange
        var client = fixture.Client;
        var token = invalidToken switch
        {
            InvalidToken.WrongIssuer => fixture.CreateToken("invalid@example.com", "OtherIssuer"),
            InvalidToken.WrongAudience => fixture.CreateToken("invalid@example.com", audience: "OtherAudience"),
            InvalidToken.WrongKey => fixture.CreateToken("invalid@example.com", signingCredentials: HostFixture.CreateForeignSigningCredentials()),
            _ => fixture.CreateToken("invalid@example.com", expires: DateTime.UtcNow.AddSeconds(-30))
        };

        // Act
        using var response = await GetAuthenticatedPageAsync(client, token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/blazor/login?returnPath=");
    }

    [Fact]
    public async Task AuthenticatedPage_WhenAnonymous_ShouldRedirectToLoginUnderPathBaseWithEncodedReturnPath()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync("blazor/app/details?tab=a&b=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/blazor/login?returnPath=%2Fblazor%2Fapp%2Fdetails%3Ftab%3Da%26b%3D1");
    }

    [Fact]
    public async Task AuthenticatedPage_WhenAnonymousRequestAcceptsJson_ShouldReturnUnauthorized()
    {
        // Arrange
        var client = fixture.Client;
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/app");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public void IsApiRequest_WhenPathIsUnderApi_ShouldBeTrue()
    {
        // Arrange
        var context = new DefaultHttpContext { Request = { PathBase = "/blazor", Path = "/api/account/users" } };

        // Act & Assert
        HostAuthentication.IsApiRequest(context.Request).Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("100.64.3.4", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("203.0.113.9", false)]
    public async Task ForwardedHeaders_ShouldApplyProtoAndForOnlyFromTrustedNetworks(string remoteAddress, bool isTrusted)
    {
        // Arrange
        var context = CreateForwardedContext(remoteAddress, HostFixture.PublicHost);

        // Act
        await RunForwardedHeadersAsync(context);

        // Assert
        context.Request.Scheme.Should().Be(isTrusted ? "https" : "http");
        context.Connection.RemoteIpAddress!.ToString().Should().Be(isTrusted ? "198.51.100.7" : remoteAddress);
        context.Request.Host.Value.Should().Be(isTrusted ? HostFixture.PublicHost : "blazor-host.internal");
    }

    [Fact]
    public async Task ForwardedHeaders_WhenHostIsNotPublicUrlHost_ShouldKeepRequestHost()
    {
        // Arrange
        var context = CreateForwardedContext("127.0.0.1", "attacker.example");

        // Act
        await RunForwardedHeadersAsync(context);

        // Assert
        context.Request.Host.Value.Should().Be("blazor-host.internal");
    }

    [Theory]
    [InlineData(HostFixture.PublicHost)]
    [InlineData("attacker.example")]
    public async Task LoginPost_WhenForwardedHostIsForged_ShouldNeverRedirectToForgedHost(string forwardedHost)
    {
        // Arrange
        var client = fixture.Client;
        var email = $"redirect-{Guid.NewGuid():N}@example.com";
        var (cookie, formToken) = await fixture.GetLoginFormAsync(client, null);
        using var request = HostFixture.CreateLoginPost(email, cookie, formToken, null);
        request.Headers.Remove("X-Forwarded-Host");
        request.Headers.Add("X-Forwarded-Host", forwardedHost);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.OriginalString;
        location.Should().NotContain("attacker.example");
        if (forwardedHost == HostFixture.PublicHost) location.Should().StartWith($"https://{HostFixture.PublicHost}/blazor/login/verify?id=");
    }

    [Fact]
    public async Task LoginPost_WhenAntiforgeryTokenBelongsToAnotherBrowser_ShouldReturnBadRequestWithoutCallingAccountApi()
    {
        // Arrange
        var client = fixture.Client;
        var email = $"cross-{Guid.NewGuid():N}@example.com";
        var (cookie, _) = await fixture.GetLoginFormAsync(client, null);
        var (_, otherBrowserFormToken) = await fixture.GetLoginFormAsync(client, null);
        using var request = HostFixture.CreateLoginPost(email, cookie, otherBrowserFormToken, null);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        fixture.AccountApiRequests.Should().NotContainKey(email);
    }

    [Fact]
    public async Task LoginPost_WhenTwoUsersPostAtTheSameTime_ShouldRelayOnlyEachRequestsOwnCredentialsAndTokens()
    {
        // Arrange
        var client = fixture.Client;
        var users = Enumerable.Range(0, 6).Select(index => $"concurrent-{index}-{Guid.NewGuid():N}@example.com").ToArray();
        var prepared = new List<(string Email, string BearerToken, string Cookie, string FormToken)>();
        foreach (var email in users)
        {
            var bearerToken = fixture.CreateToken(email);
            var (cookie, formToken) = await fixture.GetLoginFormAsync(client, bearerToken);
            prepared.Add((email, bearerToken, cookie, formToken));
        }

        // Act
        var responses = await Task.WhenAll(prepared.Select(async user =>
                {
                    using var request = HostFixture.CreateLoginPost(user.Email, user.Cookie, user.FormToken, user.BearerToken);
                    // A proxy chain: only the last hop is accepted from the trusted gateway, and only that value is relayed
                    request.Headers.Add("X-Forwarded-For", "203.0.113.9, 198.51.100.7");
                    using var response = await client.SendAsync(request);
                    return (user.Email, response.StatusCode, AccessToken: response.Headers.GetValues("x-access-token").Single());
                }
            )
        );

        // Assert
        foreach (var user in prepared)
        {
            var recorded = fixture.AccountApiRequests[user.Email];
            recorded.Authorization.Should().Be($"Bearer {user.BearerToken}");
            recorded.Cookie.Should().Be(user.Cookie);
            recorded.AntiforgeryToken.Should().Be(user.FormToken);
            recorded.ForwardedFor.Should().Be("198.51.100.7");
            recorded.ForwardedProto.Should().Be("https");
            recorded.ForwardedHost.Should().BeNull();
        }

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Redirect && response.AccessToken == $"access-{response.Email}");
    }

    [Fact]
    public async Task AuthenticatedPrerender_WhenTwoUsersRenderAtTheSameTime_ShouldContainOnlyEachUsersIdentityAndNotBeStored()
    {
        // Arrange
        var client = fixture.Client;
        var users = Enumerable.Range(0, 6).Select(index => $"prerender-{index}-{Guid.NewGuid():N}@example.com").ToArray();

        // Act
        var documents = await Task.WhenAll(users.Select(async email =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/app");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken(email));
                    using var response = await client.SendAsync(request);
                    return (Email: email, response.StatusCode, response.Headers.CacheControl, Html: await response.Content.ReadAsStringAsync());
                }
            )
        );

        // Assert
        foreach (var document in documents)
        {
            document.StatusCode.Should().Be(HttpStatusCode.OK);
            document.CacheControl!.NoStore.Should().BeTrue();
            document.Html.Should().Contain($"data-testid=\"bootstrap-email\">{document.Email}<").And.Contain($"tenant-of-{document.Email}");
            foreach (var otherUser in users.Where(email => email != document.Email))
            {
                document.Html.Should().NotContain(otherUser);
            }
        }
    }

    [Theory]
    [InlineData("blazor/login")]
    [InlineData("blazor/error?error=session_revoked")]
    public async Task DocumentsWithAntiforgeryToken_ShouldNotBeStored(string path)
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private static async Task<HttpResponseMessage> GetAuthenticatedPageAsync(HttpClient client, string bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "blazor/app");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await client.SendAsync(request);
    }

    private static DefaultHttpContext CreateForwardedContext(string remoteAddress, string forwardedHost)
    {
        var context = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse(remoteAddress) },
            Request = { Scheme = "http", Host = new HostString("blazor-host.internal") }
        };
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.7";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        return context;
    }

    private static Task RunForwardedHeadersAsync(HttpContext context)
    {
        var options = HostApplication.CreateForwardedHeadersOptions(new Uri($"https://{HostFixture.PublicHost}"));
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        return middleware.Invoke(context);
    }
}
