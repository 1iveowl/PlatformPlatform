using System.Net;
using System.Security.Claims;
using AppGateway.Middleware;
using AppGateway.Tests;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Authentication;
using SharedKernel.Authentication.TokenSigning;

namespace PublicClientSpike.Gateway.Tests;

// SPIKE CODE (T012). The production gateway's credential handling as it stands, for the cookie and bearer-only matrix.
public sealed class CredentialMatrixTests(AppGatewayApplicationFactory factory) : IClassFixture<AppGatewayApplicationFactory>
{
    [Fact]
    public async Task BearerOnlySwitchTenant_WhenAccountReturnsReplacementTokenHeaders_ShouldTurnThemIntoCookiesAndStripTheHeaders()
    {
        // Arrange
        await using var scope = factory.Services.CreateAsyncScope();
        var middleware = scope.ServiceProvider.GetRequiredService<AuthenticationCookieMiddleware>();
        var signingClient = scope.ServiceProvider.GetRequiredService<ITokenSigningClient>();
        var context = CreateHttpContext("/api/account/authentication/switch-tenant");
        context.Request.Headers.Authorization = $"Bearer {CreateSignedToken(signingClient, 5, [])}";

        // Act
        await middleware.InvokeAsync(context, downstream =>
            {
                downstream.Response.Headers[AuthenticationTokenHttpKeys.RefreshTokenHttpHeaderKey] = CreateSignedToken(signingClient, 60, []);
                downstream.Response.Headers[AuthenticationTokenHttpKeys.AccessTokenHttpHeaderKey] = CreateSignedToken(signingClient, 5, []);
                return Task.CompletedTask;
            }
        );
        await TriggerOnStartingAsync(context);

        // Assert
        context.Response.Headers.Should().NotContainKey(AuthenticationTokenHttpKeys.AccessTokenHttpHeaderKey, "a bearer-only client loses its replacement tokens today");
        context.Response.Headers.Should().NotContainKey(AuthenticationTokenHttpKeys.RefreshTokenHttpHeaderKey);
        context.Response.Headers.SetCookie.ToArray().Should().Contain(header => header!.Contains(AuthenticationTokenHttpKeys.AccessTokenCookieName));
    }

    [Fact]
    public async Task CookieAndBearer_WhenBothArePresent_ShouldForwardTheCookieSessionAndDiscardTheBearerToken()
    {
        // Arrange
        await using var scope = factory.Services.CreateAsyncScope();
        var middleware = scope.ServiceProvider.GetRequiredService<AuthenticationCookieMiddleware>();
        var signingClient = scope.ServiceProvider.GetRequiredService<ITokenSigningClient>();
        var cookieAccessToken = CreateSignedToken(signingClient, 5, [new Claim("sub", "cookie-user")]);
        var bearerAccessToken = CreateSignedToken(signingClient, 5, [new Claim("sub", "bearer-user")]);
        var context = CreateHttpContext("/api/account/users/me");
        context.Request.Headers.Cookie = $"{AuthenticationTokenHttpKeys.RefreshTokenCookieName}={CreateSignedToken(signingClient, 60, [])}; {AuthenticationTokenHttpKeys.AccessTokenCookieName}={cookieAccessToken}";
        context.Request.Headers.Authorization = $"Bearer {bearerAccessToken}";
        string? forwardedAuthorization = null;

        // Act
        await middleware.InvokeAsync(context, downstream =>
            {
                forwardedAuthorization = downstream.Request.Headers.Authorization.ToString();
                return Task.CompletedTask;
            }
        );

        // Assert
        forwardedAuthorization.Should().Be($"Bearer {cookieAccessToken}");
    }

    [Fact]
    public async Task CookieAndTokenHeaders_WhenRefreshCookieIsMixedWithAccessTokenHeader_ShouldThrow()
    {
        // Arrange
        await using var scope = factory.Services.CreateAsyncScope();
        var middleware = scope.ServiceProvider.GetRequiredService<AuthenticationCookieMiddleware>();
        var signingClient = scope.ServiceProvider.GetRequiredService<ITokenSigningClient>();
        var context = CreateHttpContext("/api/account/users/me");
        context.Request.Headers.Cookie = $"{AuthenticationTokenHttpKeys.RefreshTokenCookieName}={CreateSignedToken(signingClient, 60, [])}";
        context.Request.Headers[AuthenticationTokenHttpKeys.AccessTokenHttpHeaderKey] = CreateSignedToken(signingClient, 5, []);

        // Act
        var act = () => middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("/internal-api/account/authentication/refresh-authentication-tokens")]
    [InlineData("/api/account/../internal-api/account/authentication/refresh-authentication-tokens")]
    public async Task InternalRefresh_WhenRequestedThroughThePublicGateway_ShouldBeForbidden(string url)
    {
        // Arrange
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://app.dev.localhost") });

        // Act
        var response = await client.PostAsync(url, null);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    private static DefaultHttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext { Request = { Path = path }, Response = { Body = new MemoryStream() } };
        context.Features.Set<IHttpResponseFeature>(new CapturingResponseFeature());
        return context;
    }

    private static Task TriggerOnStartingAsync(HttpContext context)
    {
        return ((CapturingResponseFeature)context.Features.GetRequiredFeature<IHttpResponseFeature>()).TriggerOnStartingAsync();
    }

    private static string CreateSignedToken(ITokenSigningClient signingClient, int validForMinutes, Claim[] claims)
    {
        var now = DateTimeOffset.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            NotBefore = now.UtcDateTime, IssuedAt = now.UtcDateTime, Expires = now.AddMinutes(validForMinutes).UtcDateTime, Issuer = signingClient.Issuer,
            Audience = signingClient.Audience, SigningCredentials = signingClient.GetSigningCredentials(), Subject = claims.Length == 0 ? null : new ClaimsIdentity(claims)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class CapturingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStartingCallbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state)
        {
            _onStartingCallbacks.Add((callback, state));
        }

        public override void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task TriggerOnStartingAsync()
        {
            foreach (var (callback, state) in _onStartingCallbacks) await callback(state);
        }
    }
}
