using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Account.Features.Authentication.Commands;
using Account.Features.Authentication.Domain;
using Account.Features.Users.Domain;
using Account.Features.Users.Shared;
using MediatR;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SharedKernel.Authentication;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Domain;
using SharedKernel.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace PublicClientSpike.Tests.Adapter;

// SPIKE CODE (T012). A candidate public-client authorization server hosted only inside the account test host. No production
// project references this assembly. OpenIddict owns the authorization and code-exchange protocol checks (PKCE S256, code
// lifetime, redirect and client binding). The account's own Session, AuthenticationTokenService and
// RefreshAuthenticationTokensCommand issue, rotate and revoke credentials, so no second credential store exists.
public static class PublicClientAuthorizationServer
{
    public const string AuthorizePath = "/connect/authorize";
    public const string TokenPath = "/connect/token";
    public const string ApiScope = "account.api";
    public const string WebSessionClaim = "web_session_id";

    // Registered public clients (RFC 8252): a loopback redirect for desktop apps, where any port is accepted (section 7.3),
    // and a private-use URI scheme for mobile apps (section 7.1). Neither has a secret.
    public static readonly RegisteredPublicClient[] RegisteredClients =
    [
        new("native-desktop", RedirectKind.Loopback, "http://127.0.0.1/callback"),
        new("native-mobile", RedirectKind.PrivateUseScheme, "dk.platformplatform.spike:/oauth2redirect")
    ];

    public static void AddPublicClientAuthorizationServer(this IServiceCollection services, SpikeClock clock)
    {
        services.AddSingleton(clock);
        services.AddSingleton<AuthorizationCodeRedemptions>();
        services.AddSingleton<NativeSessionClients>();
        services.AddSingleton<IStartupFilter, PublicClientStartupFilter>();

        services.AddOpenIddict().AddServer(options =>
            {
                options.SetAuthorizationEndpointUris(AuthorizePath[1..]).SetTokenEndpointUris(TokenPath[1..]);
                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(1));
                // offline_access would need the OpenIddict refresh flow; the platform issues its own refresh token with every login
                options.RegisterScopes(ApiScope);
                options.EnableDegradedMode();
                options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
                options.Configure(serverOptions =>
                    {
                        serverOptions.TimeProvider = clock;
                        serverOptions.CodeChallengeMethods.Clear();
                        serverOptions.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                    }
                );
                options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough().DisableTransportSecurityRequirement();

                // Degraded mode has no client store, so client and redirect registration are validated here, before any
                // built-in handler can attach the redirect URI to an error response.
                options.AddEventHandler<ValidateAuthorizationRequestContext>(builder => builder.UseInlineHandler(context =>
                            {
                                var client = RegisteredClients.SingleOrDefault(c => c.ClientId == context.ClientId);
                                if (client is null)
                                {
                                    context.Reject(Errors.InvalidClient, "The client is not registered.");
                                    return default;
                                }

                                if (context.RedirectUri is null || !client.IsRegisteredRedirect(context.RedirectUri))
                                {
                                    context.Reject(Errors.InvalidRequest, "The redirect_uri is not registered for this client.");
                                    return default;
                                }

                                context.SetRedirectUri(context.RedirectUri);
                                return default;
                            }
                        )
                        .SetOrder(int.MinValue + 50_000)
                );

                options.AddEventHandler<ValidateTokenRequestContext>(builder => builder.UseInlineHandler(context =>
                            {
                                if (RegisteredClients.All(c => c.ClientId != context.ClientId))
                                {
                                    context.Reject(Errors.InvalidClient, "The client is not registered.");
                                    return default;
                                }

                                if (context.Request.ClientSecret is not null || context.Request.ClientAssertion is not null)
                                {
                                    context.Reject(Errors.InvalidClient, "Public clients must not present client credentials.");
                                }

                                return default;
                            }
                        )
                        .SetOrder(int.MinValue + 50_000)
                );
            }
        );
    }

    internal static async Task HandleAuthorizeAsync(HttpContext context)
    {
        // A top-level navigation in a system browser sends Sec-Fetch-Mode: navigate. A script running in a compromised web
        // page can only issue fetch or XHR requests (cors, no-cors, same-origin), so it cannot drive this endpoint.
        if (context.Request.Headers.TryGetValue("Sec-Fetch-Mode", out var fetchMode) && fetchMode.ToString() != "navigate")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No authorization request.");
        var webSession = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!webSession.Succeeded)
        {
            context.Response.Redirect($"/login?returnPath={Uri.EscapeDataString(context.Request.Path + context.Request.QueryString)}");
            return;
        }

        var userId = webSession.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var webSessionId = webSession.Principal.FindFirstValue("session_id");
        var sessionRepository = context.RequestServices.GetRequiredService<ISessionRepository>();
        var session = SessionId.TryParse(webSessionId, out var sessionId) ? await sessionRepository.GetByIdUnfilteredAsync(sessionId, context.RequestAborted) : null;
        if (session is null || session.IsRevoked || session.UserId.ToString() != userId)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, userId);
        identity.SetClaim(WebSessionClaim, session.Id.ToString());
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());

        await context.SignInAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
    }

    internal static async Task HandleCodeExchangeAsync(HttpContext context)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No token request.");
        if (!request.IsAuthorizationCodeGrantType())
        {
            await WriteErrorAsync(context, Errors.UnsupportedGrantType, "Only authorization_code and refresh_token are supported.");
            return;
        }

        // OpenIddict has already validated the code, its lifetime, the PKCE verifier, the client and the redirect URI
        var code = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var redemptions = context.RequestServices.GetRequiredService<AuthorizationCodeRedemptions>();
        if (!redemptions.TryRedeem(request.Code!))
        {
            await WriteErrorAsync(context, Errors.InvalidGrant, "The authorization code has already been redeemed.");
            return;
        }

        var cancellationToken = context.RequestAborted;
        var sessionRepository = context.RequestServices.GetRequiredService<ISessionRepository>();
        var webSession = SessionId.TryParse(code.Principal!.GetClaim(WebSessionClaim), out var webSessionId)
            ? await sessionRepository.GetByIdUnfilteredAsync(webSessionId, cancellationToken)
            : null;
        if (webSession is null || webSession.IsRevoked)
        {
            await WriteErrorAsync(context, Errors.InvalidGrant, "The web session that approved this code is no longer active.");
            return;
        }

        var userRepository = context.RequestServices.GetRequiredService<IUserRepository>();
        var user = UserId.TryParse(code.Principal.GetClaim(Claims.Subject), out var userId) ? await userRepository.GetByIdUnfilteredAsync(userId, cancellationToken) : null;
        if (user is null)
        {
            await WriteErrorAsync(context, Errors.InvalidGrant, "The user no longer exists.");
            return;
        }

        // A native login is its own session in the one session domain model, with the login method of the approving session
        var nativeSession = Session.Create(user.TenantId, user.Id, webSession.LoginMethod, context.Request.Headers.UserAgent.ToString(), context.Connection.RemoteIpAddress ?? IPAddress.Loopback);
        await sessionRepository.AddAsync(nativeSession, cancellationToken);
        var userInfo = await context.RequestServices.GetRequiredService<UserInfoFactory>().CreateUserInfoAsync(user, nativeSession.Id, cancellationToken);
        context.RequestServices.GetRequiredService<AuthenticationTokenService>().CreateAndSetAuthenticationTokens(userInfo.Value!, nativeSession.Id, nativeSession.RefreshTokenJti);
        await context.RequestServices.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        context.RequestServices.GetRequiredService<NativeSessionClients>().Bind(nativeSession.Id, request.ClientId!);

        await WriteTokenResponseAsync(context, nativeSession.Id);
    }

    // The refresh grant is answered before OpenIddict sees it: the platform refresh token is not an OpenIddict token, and
    // rotation, the grace window and replay revocation must stay in RefreshAuthenticationTokensCommand.
    internal static async Task<bool> TryHandleRefreshAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !context.Request.HasFormContentType) return false;
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (form[Parameters.GrantType] != GrantTypes.RefreshToken) return false;

        var clientId = form[Parameters.ClientId].ToString();
        if (RegisteredClients.All(c => c.ClientId != clientId) || form.ContainsKey(Parameters.ClientSecret))
        {
            await WriteErrorAsync(context, Errors.InvalidClient, "The client is not a registered public client.");
            return true;
        }

        context.Request.Headers.Authorization = $"Bearer {form[Parameters.RefreshToken]}";
        var refreshToken = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        context.Request.Headers.Remove("Authorization");
        if (!refreshToken.Succeeded)
        {
            await WriteErrorAsync(context, Errors.InvalidGrant, "The refresh token is invalid.");
            return true;
        }

        var sessionId = refreshToken.Principal!.FindFirstValue("sid");
        if (sessionId is not null && !context.RequestServices.GetRequiredService<NativeSessionClients>().IsBoundTo(sessionId, clientId))
        {
            await WriteErrorAsync(context, Errors.InvalidGrant, "The refresh token was not issued to this client.");
            return true;
        }

        context.User = refreshToken.Principal;
        var result = await context.RequestServices.GetRequiredService<IMediator>().Send(new RefreshAuthenticationTokensCommand(), context.RequestAborted);
        if (!result.IsSuccess)
        {
            context.Response.Headers.Remove(AuthenticationTokenHttpKeys.RefreshTokenHttpHeaderKey);
            context.Response.Headers.Remove(AuthenticationTokenHttpKeys.AccessTokenHttpHeaderKey);
            var reason = result.ResponseHeaders?.TryGetValue(AuthenticationTokenHttpKeys.UnauthorizedReasonHeaderKey, out var value) == true ? value : "Unknown";
            await WriteErrorAsync(context, Errors.InvalidGrant, reason);
            return true;
        }

        await WriteTokenResponseAsync(context, null);
        return true;
    }

    internal static Task WriteErrorAsync(HttpContext context, string error, string description)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new Dictionary<string, string> { [Parameters.Error] = error, [Parameters.ErrorDescription] = description });
    }

    // RFC 6749 section 5.1 body. The account API writes new tokens to response headers for the gateway to turn into cookies;
    // a public client needs them in the body instead, which is the transport change stage N makes.
    private static Task WriteTokenResponseAsync(HttpContext context, SessionId? nativeSessionId)
    {
        var headers = context.Response.Headers;
        var body = new Dictionary<string, object>
        {
            [Parameters.AccessToken] = headers[AuthenticationTokenHttpKeys.AccessTokenHttpHeaderKey].ToString(),
            [Parameters.RefreshToken] = headers[AuthenticationTokenHttpKeys.RefreshTokenHttpHeaderKey].ToString(),
            [Parameters.TokenType] = TokenTypes.Bearer,
            [Parameters.ExpiresIn] = 300
        };
        if (nativeSessionId is not null) body["session_id"] = nativeSessionId.ToString();
        headers.Remove(AuthenticationTokenHttpKeys.AccessTokenHttpHeaderKey);
        headers.Remove(AuthenticationTokenHttpKeys.RefreshTokenHttpHeaderKey);
        headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(body);
    }
}

public enum RedirectKind
{
    Loopback,
    PrivateUseScheme
}

public sealed record RegisteredPublicClient(string ClientId, RedirectKind RedirectKind, string RedirectUri)
{
    public bool IsRegisteredRedirect(string redirectUri)
    {
        if (RedirectKind == RedirectKind.PrivateUseScheme) return redirectUri == RedirectUri;

        // RFC 8252 section 7.3 and 8.3: loopback IP literal over http, any port, exact path
        var registered = new Uri(RedirectUri);
        return Uri.TryCreate(redirectUri, UriKind.Absolute, out var candidate) && candidate.Scheme == Uri.UriSchemeHttp &&
               candidate.Host == registered.Host && candidate.AbsolutePath == registered.AbsolutePath && candidate.Query == "" && candidate.Fragment == "";
    }
}

// One-use enforcement for authorization codes. Degraded mode has no token store; stage N moves this to the account database
// or a distributed cache. It holds only hashes of redeemed codes, never a credential.
public sealed class AuthorizationCodeRedemptions
{
    private readonly ConcurrentDictionary<string, byte> _redeemedCodeHashes = new();

    public bool TryRedeem(string code)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        return _redeemedCodeHashes.TryAdd(hash, 0);
    }
}

// Stand-in for the client_id column stage N adds to Session: which registered client a native session was issued to
public sealed class NativeSessionClients
{
    private readonly ConcurrentDictionary<string, string> _clientBySession = new();

    public void Bind(SessionId sessionId, string clientId)
    {
        _clientBySession[sessionId.ToString()] = clientId;
    }

    public bool IsBoundTo(string sessionId, string clientId)
    {
        return !_clientBySession.TryGetValue(sessionId, out var boundClientId) || boundClientId == clientId;
    }
}

public sealed class SpikeClock : TimeProvider
{
    private TimeSpan _offset = TimeSpan.Zero;

    public override DateTimeOffset GetUtcNow()
    {
        return System.GetUtcNow() + _offset;
    }

    public void Advance(TimeSpan duration)
    {
        _offset += duration;
    }

    public void Reset()
    {
        _offset = TimeSpan.Zero;
    }
}

internal sealed class PublicClientStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.MapWhen(context => context.Request.Path.StartsWithSegments("/connect"), branch =>
                {
                    branch.Use(async (context, nextMiddleware) =>
                        {
                            if (context.Request.Path == PublicClientAuthorizationServer.TokenPath && await PublicClientAuthorizationServer.TryHandleRefreshAsync(context)) return;
                            await nextMiddleware(context);
                        }
                    );
                    branch.UseAuthentication();
                    branch.Run(context => context.Request.Path == PublicClientAuthorizationServer.AuthorizePath
                        ? PublicClientAuthorizationServer.HandleAuthorizeAsync(context)
                        : PublicClientAuthorizationServer.HandleCodeExchangeAsync(context)
                    );
                }
            );
            next(app);
        };
    }
}
