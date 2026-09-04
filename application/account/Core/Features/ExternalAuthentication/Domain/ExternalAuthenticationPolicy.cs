namespace Account.Features.ExternalAuthentication.Domain;

/// <summary>
///     Decides which flow types each external provider is allowed to run. This is the only place the answer is
///     written down, and it is a security boundary rather than a presentation concern: the provider factory and the
///     generic <c>/{provider}/{flow}/...</c> routes make every combination reachable as soon as a provider is
///     registered, whether or not the user interface links to it.
///     MitID is verification only. It resolves no account and creates no session, so allowing it to reach the login
///     or signup callbacks would let a national identity be used for something it was never bound for.
/// </summary>
public static class ExternalAuthenticationPolicy
{
    /// <summary>
    ///     The level of assurance a verification must reach. The provider requests it and the verification handler
    ///     enforces it, because acr_values is a hint in OpenID Connect rather than a requirement: a provider is free to
    ///     satisfy the request at a lower level, and without the check a weaker authentication would be recorded as
    ///     substantial, which is the one claim the whole feature rests on. Enforcing it in the handler rather than in
    ///     the provider gives the person the specific outcome instead of a generic authentication failure.
    /// </summary>
    public const IdentityAssuranceLevel RequiredAssuranceLevel = IdentityAssuranceLevel.Substantial;

    public static bool IsFlowSupported(ExternalProviderType providerType, ExternalLoginType loginType)
    {
        return providerType switch
        {
            ExternalProviderType.Google => loginType is ExternalLoginType.Login or ExternalLoginType.Signup,
            ExternalProviderType.Entra => loginType is ExternalLoginType.Login or ExternalLoginType.Signup,
            ExternalProviderType.MitId => loginType is ExternalLoginType.Verification,
            _ => false
        };
    }

    /// <summary>
    ///     A flow bound to the user who started it. Only verification is, because it is the only flow that runs from
    ///     an already authenticated session and writes to an account that is known before the provider replies.
    /// </summary>
    public static bool RequiresAuthenticatedUser(ExternalLoginType loginType)
    {
        return loginType == ExternalLoginType.Verification;
    }
}
