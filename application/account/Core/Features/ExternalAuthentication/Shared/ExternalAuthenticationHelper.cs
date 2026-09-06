using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth;
using Microsoft.AspNetCore.Http;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.Telemetry;

namespace Account.Features.ExternalAuthentication.Shared;

internal sealed record CallbackValidationResult(
    bool IsSuccess,
    ExternalLogin ExternalLogin,
    ExternalLoginCookie Cookie,
    OAuthUserProfile? UserProfile,
    Result<string>? ErrorResult
)
{
    public static CallbackValidationResult Success(ExternalLogin externalLogin, ExternalLoginCookie cookie, OAuthUserProfile userProfile)
    {
        return new CallbackValidationResult(true, externalLogin, cookie, userProfile, null);
    }

    public static CallbackValidationResult Failure(ExternalLogin externalLogin, ExternalLoginCookie cookie, Result<string> errorResult)
    {
        return new CallbackValidationResult(false, externalLogin, cookie, null, errorResult);
    }
}

public sealed class ExternalAuthenticationHelper(
    IExternalLoginRepository externalLoginRepository,
    OAuthProviderFactory oauthProviderFactory,
    ExternalAuthenticationService externalAuthenticationService,
    IHttpContextAccessor httpContextAccessor,
    IExecutionContext executionContext,
    ITelemetryEventsCollector events,
    TimeProvider timeProvider,
    ILogger<ExternalAuthenticationHelper> logger
)
{
    /// <summary>
    ///     How far before the flow started an identity provider's reported authentication instant may fall before it
    ///     is treated as a replayed session rather than clock drift between us and the provider.
    /// </summary>
    private const int AuthenticationInstantClockSkewSeconds = 30;

    internal async Task<CallbackValidationResult> ValidateCallback(
        string? code,
        string? state,
        string? error,
        string? errorDescription,
        ExternalProviderType providerType,
        ExternalLoginType loginType,
        CancellationToken cancellationToken
    )
    {
        var externalLoginCookie = externalAuthenticationService.GetExternalLoginCookie();
        var externalLoginIdFromState = externalAuthenticationService.GetExternalLoginIdFromState(state);

        if (externalLoginIdFromState is null && externalLoginCookie is null)
        {
            logger.LogWarning("Missing state and cookie");
            return FailedRedirect(null!, externalLoginCookie!, ExternalLoginResult.InvalidState, loginType);
        }

        Activity.Current?.SetTag("flow_id", externalLoginIdFromState?.ToString() ?? externalLoginCookie?.ExternalLoginId.ToString());

        if (externalLoginIdFromState is null)
        {
            logger.LogWarning("Missing external login ID from state");
            return FailedRedirect(null!, externalLoginCookie!, ExternalLoginResult.InvalidState, loginType);
        }

        if (externalLoginCookie is null)
        {
            logger.LogWarning("Replay detected for flow '{FlowId}' - session cookie missing", externalLoginIdFromState);
            return FailedRedirect(null!, externalLoginCookie!, ExternalLoginResult.LoginReplayDetected, loginType);
        }

        var externalLogin = await externalLoginRepository.GetByIdAsync(externalLoginIdFromState, cancellationToken);
        if (externalLogin is null)
        {
            logger.LogWarning("Session not found for external login '{ExternalLoginId}'", externalLoginIdFromState);
            return FailedRedirect(null!, externalLoginCookie, ExternalLoginResult.SessionNotFound, loginType);
        }

        if (externalLoginIdFromState != externalLoginCookie.ExternalLoginId)
        {
            logger.LogWarning("Flow ID mismatch for external login '{ExternalLoginId}'", externalLoginIdFromState);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.FlowIdMismatch, loginType);
        }

        if (!externalAuthenticationService.ValidateBrowserFingerprint(externalLoginCookie.FingerprintHash))
        {
            logger.LogWarning("Session hijacking detected for external login '{ExternalLoginId}'", externalLoginIdFromState);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.SessionHijackingDetected, loginType);
        }

        if (externalLogin.IsExpired(timeProvider.GetUtcNow()))
        {
            logger.LogWarning("Login expired for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.LoginExpired, loginType);
        }

        if (externalLogin.IsConsumed)
        {
            logger.LogWarning("Login already completed for external login '{ExternalLoginId}'", externalLoginIdFromState);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.LoginAlreadyCompleted, loginType);
        }

        // The route only decides which handler runs; every decision below is made on the persisted flow. The checks
        // sit before the code exchange so a flow that must be rejected never burns an authorization code.
        if (externalLogin.Type != loginType)
        {
            logger.LogWarning("Flow type mismatch for external login '{ExternalLoginId}' started as '{FlowType}' and presented to a '{CallbackType}' callback", externalLogin.Id, externalLogin.Type, loginType);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.FlowNotSupported, loginType);
        }

        if (externalLogin.ProviderType != providerType)
        {
            logger.LogWarning("Provider mismatch for external login '{ExternalLoginId}' started with '{FlowProvider}' and presented to a '{CallbackProvider}' callback", externalLogin.Id, externalLogin.ProviderType, providerType);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.FlowNotSupported, loginType);
        }

        if (!ExternalAuthenticationPolicy.IsFlowSupported(externalLogin.ProviderType, externalLogin.Type))
        {
            logger.LogWarning("Provider '{ProviderType}' does not support the '{FlowType}' flow", externalLogin.ProviderType, externalLogin.Type);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.FlowNotSupported, loginType);
        }

        // Checked here as well as at the start, so a flow already in flight cannot be completed after the deployment
        // stops permitting that use of the provider
        if (!oauthProviderFactory.IsFlowEnabled(externalLogin.ProviderType, externalLogin.Type))
        {
            logger.LogWarning("Provider '{ProviderType}' is not enabled for the '{FlowType}' flow", externalLogin.ProviderType, externalLogin.Type);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.FlowNotSupported, loginType);
        }

        var httpContext = httpContextAccessor.HttpContext!;
        var useMockProvider = oauthProviderFactory.ShouldUseMockProvider(httpContext);

        // The mock is chosen per request from a cookie, so without this a flow started against the real provider
        // could be completed against the mock, where the caller picks the provider user id
        if (useMockProvider != externalLogin.UsedMockProvider)
        {
            logger.LogWarning("Provider selection changed between start and callback for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.FlowNotSupported, loginType);
        }

        if (ExternalAuthenticationPolicy.RequiresAuthenticatedUser(externalLogin.Type))
        {
            var bindingResult = ValidateUserBinding(externalLogin, externalLoginCookie, loginType);
            if (bindingResult is not null) return bindingResult;
        }

        if (!string.IsNullOrEmpty(error))
        {
            logger.LogWarning("OAuth error received: '{Error}' - '{ErrorDescription}'", error, errorDescription);
            return OAuthErrorRedirect(externalLogin, externalLoginCookie, error, loginType);
        }

        if (string.IsNullOrEmpty(code))
        {
            logger.LogWarning("Authorization code missing from OAuth callback");
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.CodeExchangeFailed, loginType);
        }

        var oauthProvider = oauthProviderFactory.GetProvider(externalLogin.ProviderType, useMockProvider);
        if (oauthProvider is null)
        {
            logger.LogWarning("Provider '{ProviderType}' not configured", externalLogin.ProviderType);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.CodeExchangeFailed, loginType);
        }

        var redirectUri = ExternalAuthenticationService.GetRedirectUri(externalLogin.ProviderType, loginType);
        var tokenResponse = await oauthProvider.ExchangeCodeForTokensAsync(code, externalLogin.CodeVerifier, redirectUri, cancellationToken);
        if (tokenResponse is null)
        {
            logger.LogWarning("Token exchange failed for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.CodeExchangeFailed, loginType);
        }

        var userProfile = await oauthProvider.GetUserProfileAsync(tokenResponse, cancellationToken);
        if (userProfile is null)
        {
            logger.LogWarning("Failed to get user profile for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.CodeExchangeFailed, loginType);
        }

        // A profile without an email has nothing to verify; the login handler resolves such a user by identity alone
        if (userProfile.Email is not null && !userProfile.EmailVerified)
        {
            logger.LogWarning("Email not verified for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.CodeExchangeFailed, loginType);
        }

        if (userProfile.Nonce != externalLogin.Nonce)
        {
            logger.LogWarning("Nonce mismatch for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.NonceMismatch, loginType);
        }

        // A provider that reports when the person authenticated must report an authentication that happened during
        // this flow. Otherwise a cached single sign-on session would satisfy a verification with an authentication
        // from weeks ago, which is exactly what the freshness of a verification is supposed to exclude.
        if (userProfile.AuthenticationInstant is not null && !IsWithinFlowWindow(userProfile.AuthenticationInstant.Value, externalLogin))
        {
            logger.LogWarning("Stale authentication for external login '{ExternalLoginId}': the provider reports an authentication outside this flow's window", externalLogin.Id);
            return FailedRedirect(externalLogin, externalLoginCookie, ExternalLoginResult.StaleAuthentication, loginType);
        }

        return CallbackValidationResult.Success(externalLogin, externalLoginCookie, userProfile);
    }

    /// <summary>
    ///     An authentication belongs to this flow when it happened between the flow's start and now, each side widened
    ///     by a small clock skew. The upper bound matters too: an instant in the future would be persisted as evidence
    ///     and make the verification look fresh for longer than it is.
    /// </summary>
    private bool IsWithinFlowWindow(DateTimeOffset authenticationInstant, ExternalLogin externalLogin)
    {
        var earliest = externalLogin.CreatedAt.AddSeconds(-AuthenticationInstantClockSkewSeconds);
        var latest = timeProvider.GetUtcNow().AddSeconds(AuthenticationInstantClockSkewSeconds);
        return authenticationInstant >= earliest && authenticationInstant <= latest;
    }

    /// <summary>
    ///     Confirms that the flow is being completed by the user who started it. The data protected cookie is the
    ///     authority, because it is SameSite=Lax and therefore survives the identity provider's cross-site redirect.
    ///     The access token cookie is SameSite=Strict and does not, so the execution context is corroboration only;
    ///     when it reports no user the gateway lost the session rather than someone else presenting the callback, and
    ///     that is a different, retryable outcome.
    /// </summary>
    private CallbackValidationResult? ValidateUserBinding(ExternalLogin externalLogin, ExternalLoginCookie cookie, ExternalLoginType loginType)
    {
        if (cookie.UserId is null || cookie.UserId != externalLogin.UserId)
        {
            logger.LogWarning("User binding mismatch for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, cookie, ExternalLoginResult.VerificationUserMismatch, loginType);
        }

        // The cookie has already matched, so this gate buys no identification and costs availability: a callback whose
        // session cannot be re-established is refused even though the person did authenticate. That is deliberate. A
        // session revoked while the flow was in progress, say by the account's real owner, must not be able to finish
        // an action that changes who the account belongs to, and the profile page the flow returns to needs the session
        // anyway. Refresh tokens outlive the five-minute flow by days, so the cost is a rare retry.
        if (!executionContext.UserInfo.IsAuthenticated)
        {
            logger.LogWarning("No authenticated session on the callback for external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, cookie, ExternalLoginResult.VerificationSessionLost, loginType);
        }

        if (executionContext.UserInfo.Id != externalLogin.UserId)
        {
            logger.LogWarning("Authenticated user does not match the user bound to external login '{ExternalLoginId}'", externalLogin.Id);
            return FailedRedirect(externalLogin, cookie, ExternalLoginResult.VerificationUserMismatch, loginType);
        }

        return null;
    }

    private CallbackValidationResult FailedRedirect(
        ExternalLogin? externalLogin,
        ExternalLoginCookie cookie,
        ExternalLoginResult loginResult,
        ExternalLoginType loginType
    )
    {
        var timeInSeconds = 0;

        if (externalLogin is not null)
        {
            timeInSeconds = (int)(timeProvider.GetUtcNow() - externalLogin.CreatedAt).TotalSeconds;
            if (!externalLogin.IsConsumed)
            {
                externalLogin.MarkFailed(loginResult);
                externalLoginRepository.Update(externalLogin);
            }
        }

        CollectFailedEvent(loginType, externalLogin, loginResult, timeInSeconds, null);

        var oidcError = ExternalAuthenticationService.MapToOidcError(loginResult);
        var referenceId = externalLogin?.Id.ToString() ?? Activity.Current?.TraceId.ToString();
        var redirectUrl = $"/error?error={oidcError}&id={referenceId}";

        var errorResult = Result<string>.Redirect(redirectUrl);
        return CallbackValidationResult.Failure(externalLogin!, cookie, errorResult);
    }

    private CallbackValidationResult OAuthErrorRedirect(
        ExternalLogin externalLogin,
        ExternalLoginCookie cookie,
        string oauthError,
        ExternalLoginType loginType
    )
    {
        var timeInSeconds = (int)(timeProvider.GetUtcNow() - externalLogin.CreatedAt).TotalSeconds;
        if (!externalLogin.IsConsumed)
        {
            externalLogin.MarkFailed(ExternalLoginResult.IdentityProviderError);
            externalLoginRepository.Update(externalLogin);
        }

        CollectFailedEvent(loginType, externalLogin, ExternalLoginResult.IdentityProviderError, timeInSeconds, oauthError);

        var sanitizedError = Uri.EscapeDataString(oauthError);
        var errorResult = Result<string>.Redirect($"/error?error={sanitizedError}&id={externalLogin.Id}");
        return CallbackValidationResult.Failure(externalLogin, cookie, errorResult);
    }

    private void CollectFailedEvent(ExternalLoginType loginType, ExternalLogin? externalLogin, ExternalLoginResult loginResult, int timeInSeconds, string? oauthError)
    {
        TelemetryEvent telemetryEvent = loginType switch
        {
            ExternalLoginType.Login => new ExternalLoginFailed(externalLogin?.Id, loginResult, timeInSeconds, oauthError),
            ExternalLoginType.Signup => new ExternalSignupFailed(externalLogin?.Id, loginResult, timeInSeconds, oauthError),
            ExternalLoginType.Verification => new ExternalVerificationFailed(externalLogin?.Id, loginResult, timeInSeconds, oauthError),
            _ => throw new UnreachableException()
        };

        events.CollectEvent(telemetryEvent);
    }
}
