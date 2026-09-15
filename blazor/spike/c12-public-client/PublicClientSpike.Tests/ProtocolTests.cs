using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PublicClientSpike.Tests.Harness;
using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel.Authentication.TokenSigning;
using SharedKernel.Tests.Persistence;

namespace PublicClientSpike.Tests;

// SPIKE CODE (T012). RFC 8252 authorization code flow with S256 PKCE against the candidate server and the real account API.
public sealed class ProtocolTests(PublicClientSpikeFactory factory) : PublicClientSpikeTest(factory)
{
    [Theory]
    [InlineData(NativeClientHarness.DesktopClientId, NativeClientHarness.DesktopRedirectUri)]
    [InlineData(NativeClientHarness.DesktopClientId, "http://127.0.0.1:61001/callback")]
    [InlineData(NativeClientHarness.MobileClientId, NativeClientHarness.MobileRedirectUri)]
    public async Task CodeFlow_WhenRegisteredRedirectAndS256_ShouldIssueNativeSessionTokensThatCallTheApiAndRefresh(string clientId, string redirectUri)
    {
        // Arrange
        var pkce = Pkce.Create();
        var code = await Harness.GetCodeAsync(OwnerWebAccessToken(), pkce, clientId, redirectUri);

        // Act
        var exchange = await Harness.ExchangeAsync(code, pkce.Verifier, clientId, redirectUri);
        var tokens = await NativeClientHarness.ReadTokensAsync(exchange);
        using var apiClient = Harness.CreateClient(tokens.AccessToken);
        var me = await apiClient.GetAsync("/api/account/users/me");
        var refreshed = await Harness.RefreshTokensAsync(tokens.RefreshToken, clientId);
        using var refreshedClient = Harness.CreateClient(refreshed.AccessToken);
        var meAfterRefresh = await refreshedClient.GetAsync("/api/account/users/me");

        // Assert
        exchange.Headers.Contains("Set-Cookie").Should().BeFalse();
        exchange.Headers.CacheControl!.NoStore.Should().BeTrue();
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(me)).GetProperty("email").GetString().Should().Be(DatabaseSeeder.Tenant1Owner.Email);
        tokens.SessionId.Should().NotBe(DatabaseSeeder.Tenant1OwnerSession.Id.ToString());
        Claim(tokens.AccessToken, "session_id").Should().Be(tokens.SessionId);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE id = @id AND revoked_at IS NULL", [new { id = tokens.SessionId }]).Should().Be(1);
        Claim(refreshed.RefreshToken, "ver").Should().Be("2");
        meAfterRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Exchange_WhenVerifierIsWrong_ShouldRejectWithInvalidGrant()
    {
        // Arrange
        var pkce = Pkce.Create();
        var code = await Harness.GetCodeAsync(OwnerWebAccessToken(), pkce);

        // Act
        var response = await Harness.ExchangeAsync(code, Pkce.Create().Verifier);

        // Assert
        await AssertErrorAsync(response, "invalid_grant");
    }

    [Fact]
    public async Task Exchange_WhenCodeIsReused_ShouldRejectTheSecondRedemption()
    {
        // Arrange
        var pkce = Pkce.Create();
        var code = await Harness.GetCodeAsync(OwnerWebAccessToken(), pkce);
        var first = await Harness.ExchangeAsync(code, pkce.Verifier);

        // Act
        var second = await Harness.ExchangeAsync(code, pkce.Verifier);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertErrorAsync(second, "invalid_grant");
    }

    [Fact]
    public async Task Exchange_WhenCodeHasExpired_ShouldRejectWithInvalidGrant()
    {
        // Arrange
        var pkce = Pkce.Create();
        var code = await Harness.GetCodeAsync(OwnerWebAccessToken(), pkce);
        Factory.Clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        var response = await Harness.ExchangeAsync(code, pkce.Verifier);

        // Assert
        await AssertErrorAsync(response, "invalid_grant");
    }

    [Theory]
    [InlineData(NativeClientHarness.DesktopClientId, "http://localhost:53817/callback")]
    [InlineData(NativeClientHarness.DesktopClientId, "https://127.0.0.1:53817/callback")]
    [InlineData(NativeClientHarness.DesktopClientId, "http://127.0.0.1:53817/other")]
    [InlineData(NativeClientHarness.DesktopClientId, "https://attacker.example/callback")]
    [InlineData(NativeClientHarness.MobileClientId, "dk.platformplatform.spike:/other")]
    [InlineData(NativeClientHarness.DesktopClientId, NativeClientHarness.MobileRedirectUri)]
    public async Task Authorize_WhenRedirectIsNotRegistered_ShouldRejectWithoutRedirecting(string clientId, string redirectUri)
    {
        // Act
        var response = await Harness.AuthorizeAsync(OwnerWebAccessToken(), NativeClientHarness.AuthorizeParameters(Pkce.Create(), clientId, redirectUri));

        // Assert
        response.Headers.Location.Should().BeNull();
        await AssertErrorAsync(response, "invalid_request");
    }

    [Fact]
    public async Task Exchange_WhenRedirectDiffersFromAuthorization_ShouldRejectWithInvalidGrant()
    {
        // Arrange
        var pkce = Pkce.Create();
        var code = await Harness.GetCodeAsync(OwnerWebAccessToken(), pkce);

        // Act
        var response = await Harness.ExchangeAsync(code, pkce.Verifier, redirectUri: "http://127.0.0.1:1/callback");

        // Assert
        await AssertErrorAsync(response, "invalid_grant");
    }

    [Fact]
    public async Task Exchange_WhenAnotherRegisteredClientPresentsTheCode_ShouldRejectWithInvalidGrant()
    {
        // Arrange
        var pkce = Pkce.Create();
        var code = await Harness.GetCodeAsync(OwnerWebAccessToken(), pkce);

        // Act
        var response = await Harness.ExchangeAsync(code, pkce.Verifier, NativeClientHarness.MobileClientId);

        // Assert
        await AssertErrorAsync(response, "invalid_grant");
    }

    [Fact]
    public async Task Authorize_WhenClientIsUnknown_ShouldRejectWithInvalidClient()
    {
        // Act
        var response = await Harness.AuthorizeAsync(OwnerWebAccessToken(), NativeClientHarness.AuthorizeParameters(Pkce.Create(), "unknown-client"));

        // Assert
        response.Headers.Location.Should().BeNull();
        await AssertErrorAsync(response, "invalid_client");
    }

    [Theory]
    [InlineData("code_challenge_method", "plain")]
    [InlineData("code_challenge_method", null)]
    [InlineData("code_challenge", null)]
    [InlineData("scope", "account.api admin.api")]
    public async Task Authorize_WhenPkceIsWeakOrMissingOrScopeIsUnregistered_ShouldNotIssueACode(string parameter, string? value)
    {
        // Arrange
        var parameters = NativeClientHarness.AuthorizeParameters(Pkce.Create());
        parameters[parameter] = value;
        if (value is null) parameters.Remove(parameter);

        // Act
        var response = await Harness.AuthorizeAsync(OwnerWebAccessToken(), parameters);

        // Assert
        response.Headers.Location?.ToString().Should().NotContain("code=");
    }

    [Fact]
    public async Task Exchange_WhenClientSecretIsPresented_ShouldRejectWithInvalidClient()
    {
        // Arrange
        var pkce = Pkce.Create();
        var code = await Harness.GetCodeAsync(OwnerWebAccessToken(), pkce);

        // Act
        var response = await Harness.ExchangeAsync(code, pkce.Verifier, clientSecret: "embedded-secret");

        // Assert
        await AssertErrorAsync(response, "invalid_client");
    }

    [Fact]
    public async Task Api_WhenPlatformSignedTokenHasAnotherAudience_ShouldReturnUnauthorized()
    {
        // Arrange
        var signingClient = WebApplicationServices.GetRequiredService<ITokenSigningClient>();
        var now = DateTimeOffset.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", DatabaseSeeder.Tenant1Owner.Id.ToString())]), IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime, Expires = now.AddMinutes(5).UtcDateTime, Issuer = signingClient.Issuer, Audience = "https://another-api.example",
            SigningCredentials = signingClient.GetSigningCredentials()
        };
        var wrongAudienceToken = new JsonWebTokenHandler().CreateToken(descriptor);
        using var client = Harness.CreateClient(wrongAudienceToken);

        // Act
        var response = await client.GetAsync("/api/account/users/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WhenAccessTokenIsPresentedAsRefreshToken_ShouldRejectWithInvalidGrant()
    {
        // Arrange
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var response = await Harness.RefreshAsync(tokens.AccessToken);

        // Assert
        await AssertErrorAsync(response, "invalid_grant");
    }

    [Fact]
    public async Task Refresh_WhenAnotherClientPresentsTheRefreshToken_ShouldRejectWithInvalidGrant()
    {
        // Arrange
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var response = await Harness.RefreshAsync(tokens.RefreshToken, NativeClientHarness.MobileClientId);

        // Assert
        await AssertErrorAsync(response, "invalid_grant");
    }

    [Theory]
    [InlineData("cors")]
    [InlineData("no-cors")]
    [InlineData("same-origin")]
    public async Task Authorize_WhenScriptInCompromisedWebPageFetchesIt_ShouldBeForbidden(string fetchMode)
    {
        // Act
        var response = await Harness.AuthorizeAsync(OwnerWebAccessToken(), NativeClientHarness.AuthorizeParameters(Pkce.Create()), fetchMode);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Authorize_WhenWebSessionIsRevokedOrAbsent_ShouldNotIssueACode()
    {
        // Arrange
        Connection.Update("sessions", "id", DatabaseSeeder.Tenant1MemberSession.Id.ToString(), [("revoked_at", TimeProvider.GetUtcNow()), ("revoked_reason", "Revoked")]);
        using var anonymous = Harness.CreateClient();

        // Act
        var revoked = await Harness.AuthorizeAsync(MemberWebAccessToken(), NativeClientHarness.AuthorizeParameters(Pkce.Create()));
        var absent = await anonymous.GetAsync($"/connect/authorize?{string.Join('&', NativeClientHarness.AuthorizeParameters(Pkce.Create()).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"))}");

        // Assert
        revoked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        absent.StatusCode.Should().Be(HttpStatusCode.Redirect);
        absent.Headers.Location!.ToString().Should().StartWith("/login?returnPath=");
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string error)
    {
        response.StatusCode.Should().Be(error == "invalid_client" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest);
        (await NativeClientHarness.ReadErrorAsync(response)).Should().Be(error);
    }
}

// The production account host, without the spike adapter, has no authorization endpoints
public sealed class NotExposedTests(AccountTestFactoryWithAntiforgery factory)
    : Account.Tests.EndpointBaseTest<Account.Database.AccountDbContext>(factory), IClassFixture<AccountTestFactoryWithAntiforgery>
{
    [Theory]
    [InlineData("/connect/authorize?client_id=native-desktop&response_type=code")]
    [InlineData("/connect/token")]
    public async Task ProductionAccountHost_WhenAuthorizationEndpointIsRequested_ShouldNotHandleIt(string url)
    {
        // Arrange
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        // Act
        var get = await client.GetAsync(url);
        var post = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = "x" }));

        // Assert
        get.Headers.Location.Should().BeNull();
        (await get.Content.ReadAsStringAsync()).Should().NotContain("invalid_");
        post.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await post.Content.ReadAsStringAsync()).Should().NotContain("access_token");
    }
}

public sealed class AccountTestFactoryWithAntiforgery : Account.Tests.AccountWebApplicationFactory
{
    public AccountTestFactoryWithAntiforgery()
    {
        Environment.SetEnvironmentVariable("BypassAntiforgeryValidation", "false");
    }
}
