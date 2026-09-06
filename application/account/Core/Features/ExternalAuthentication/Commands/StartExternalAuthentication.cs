using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Cqrs;
using SharedKernel.Domain;
using SharedKernel.OpenIdConnect;
using SharedKernel.Telemetry;

namespace Account.Features.ExternalAuthentication.Commands;

[PublicAPI]
public sealed record StartExternalLoginCommand(TenantId? PreferredTenantId = null) : ICommand, IRequest<Result<string>>
{
    [JsonIgnore]
    public ExternalProviderType ProviderType { get; init; }
}

public sealed class StartExternalLoginHandler(
    IExternalLoginRepository externalLoginRepository,
    OAuthProviderFactory oauthProviderFactory,
    ExternalAuthenticationService externalAuthenticationService,
    IHttpContextAccessor httpContextAccessor,
    ITelemetryEventsCollector events
) : IRequestHandler<StartExternalLoginCommand, Result<string>>
{
    public async Task<Result<string>> Handle(StartExternalLoginCommand command, CancellationToken cancellationToken)
    {
        var result = await StartExternalAuthenticationHelper.StartFlow(
            command.ProviderType, ExternalLoginType.Login, command.PreferredTenantId, null, null,
            externalLoginRepository, oauthProviderFactory, externalAuthenticationService, httpContextAccessor, events, cancellationToken
        );

        return result.IsSuccess ? Result<string>.Redirect(result.Value!) : result;
    }
}

[PublicAPI]
public sealed record StartExternalSignupCommand : ICommand, IRequest<Result<string>>
{
    [JsonIgnore]
    public ExternalProviderType ProviderType { get; init; }
}

public sealed class StartExternalSignupHandler(
    IExternalLoginRepository externalLoginRepository,
    OAuthProviderFactory oauthProviderFactory,
    ExternalAuthenticationService externalAuthenticationService,
    IHttpContextAccessor httpContextAccessor,
    ITelemetryEventsCollector events
) : IRequestHandler<StartExternalSignupCommand, Result<string>>
{
    public async Task<Result<string>> Handle(StartExternalSignupCommand command, CancellationToken cancellationToken)
    {
        var result = await StartExternalAuthenticationHelper.StartFlow(
            command.ProviderType, ExternalLoginType.Signup, null, null, null,
            externalLoginRepository, oauthProviderFactory, externalAuthenticationService, httpContextAccessor, events, cancellationToken
        );

        return result.IsSuccess ? Result<string>.Redirect(result.Value!) : result;
    }
}

/// <summary>
///     The user a flow is bound to. Only the verification flow has one, because it is the only flow started from an
///     already authenticated session against an account that is known before the provider replies.
/// </summary>
internal sealed record ExternalLoginActor(UserId UserId, TenantId TenantId, SessionId? SessionId);

internal static class StartExternalAuthenticationHelper
{
    /// <summary>
    ///     Creates the flow and returns the provider's authorization URL. The caller decides how to deliver it: login
    ///     and signup redirect the browser straight away, while verification is started by an authenticated request
    ///     and hands the URL back so the single page application navigates to it.
    /// </summary>
    public static async Task<Result<string>> StartFlow(
        ExternalProviderType providerType,
        ExternalLoginType loginType,
        TenantId? preferredTenantId,
        ExternalLoginActor? actor,
        string? returnPath,
        IExternalLoginRepository externalLoginRepository,
        OAuthProviderFactory oauthProviderFactory,
        ExternalAuthenticationService externalAuthenticationService,
        IHttpContextAccessor httpContextAccessor,
        ITelemetryEventsCollector events,
        CancellationToken cancellationToken
    )
    {
        if (!ExternalAuthenticationPolicy.IsFlowSupported(providerType, loginType))
        {
            return Result<string>.BadRequest($"Provider '{providerType}' does not support the '{loginType}' flow.");
        }

        var httpContext = httpContextAccessor.HttpContext!;
        var useMockProvider = oauthProviderFactory.ShouldUseMockProvider(httpContext);

        var oauthProvider = oauthProviderFactory.GetProvider(providerType, useMockProvider);
        if (oauthProvider is null)
        {
            return Result<string>.BadRequest($"Provider '{providerType}' is not configured.");
        }

        var codeVerifier = PkceUtilities.GenerateCodeVerifier();
        var codeChallenge = PkceUtilities.GenerateCodeChallenge(codeVerifier);
        var nonce = NonceUtilities.GenerateNonce();

        var browserFingerprint = externalAuthenticationService.GenerateBrowserFingerprintHash();

        var externalLogin = ExternalLogin.Create(
            loginType, providerType, codeVerifier, nonce, browserFingerprint, useMockProvider, actor?.UserId, actor?.TenantId, actor?.SessionId
        );
        await externalLoginRepository.AddAsync(externalLogin, cancellationToken);

        var stateToken = externalAuthenticationService.ProtectState(externalLogin.Id);
        externalAuthenticationService.SetExternalLoginCookie(externalLogin.Id, preferredTenantId, actor?.UserId);

        // Login and signup are started by a plain link and carry the return path in the query string; a command
        // started flow passes it directly. ReturnPathHelper rejects anything that is not a relative path.
        var effectiveReturnPath = returnPath ?? httpContext.Request.Query["ReturnPath"].ToString();
        if (!string.IsNullOrEmpty(effectiveReturnPath))
        {
            ReturnPathHelper.SetReturnPathCookie(httpContext, effectiveReturnPath);
        }

        var locale = httpContext.Request.Query["Locale"].ToString();
        if (!string.IsNullOrEmpty(locale))
        {
            externalAuthenticationService.SetLocaleCookie(locale);
        }

        var redirectUri = ExternalAuthenticationService.GetRedirectUri(providerType, loginType);
        var authorizationUrl = oauthProvider.BuildAuthorizationUrl(stateToken, codeChallenge, nonce, redirectUri);

        TelemetryEvent telemetryEvent = loginType switch
        {
            ExternalLoginType.Login => new ExternalLoginStarted(providerType),
            ExternalLoginType.Signup => new ExternalSignupStarted(providerType),
            ExternalLoginType.Verification => new ExternalVerificationStarted(providerType),
            _ => throw new UnreachableException()
        };
        events.CollectEvent(telemetryEvent);

        return authorizationUrl;
    }
}
