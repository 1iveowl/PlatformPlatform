namespace Account.Features.ExternalAuthentication.Domain;

/// <summary>
///     Decides which flow types each external provider is allowed to run. This is the only place the answer is
///     written down, and it is a security boundary rather than a presentation concern: the provider factory and the
///     generic <c>/{provider}/{flow}/...</c> routes make every combination reachable as soon as a provider is
///     registered, whether or not the user interface links to it.
///     MitID verifies an identity, and once an identity is verified it may also log in with it, but it never signs
///     up. Signup would create an account from a national identity alone, with no email to reach the person at, so
///     allowing it to reach the signup callbacks would bind a national identity to something it was never meant for.
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
            ExternalProviderType.MitId => loginType is ExternalLoginType.Login or ExternalLoginType.Verification,
            _ => false
        };
    }

    /// <summary>
    ///     Whether a provider vouches for an email address. This is a fact about the provider rather than about a
    ///     flow: MitID through Idura requests only the openid scope and reads no email claim, so it returns none for
    ///     every flow it runs. Account resolution uses it to decide whether an email may pick the account, and the
    ///     mock provider uses it to shape the profile it returns. Deriving either from a flow permission instead
    ///     would tie the shape of a provider's reply to what the product currently lets it do.
    /// </summary>
    public static bool SuppliesEmail(ExternalProviderType providerType)
    {
        // Named positively so that a provider added without thought supplies no email, which is the safe default:
        // it can then resolve an account by its own identifier and by nothing else.
        return providerType is ExternalProviderType.Google or ExternalProviderType.Entra;
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
