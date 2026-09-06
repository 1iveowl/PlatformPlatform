using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.Domain;

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalProviderType
{
    Google,
    Entra,
    MitId
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalLoginType
{
    Login,
    Signup,
    Verification
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalLoginResult
{
    Success,
    IdentityProviderError,
    InvalidState,
    LoginReplayDetected,
    SessionNotFound,
    FlowIdMismatch,
    SessionHijackingDetected,
    LoginExpired,
    LoginAlreadyCompleted,
    CodeExchangeFailed,
    EmailNotProvided,
    NonceMismatch,
    IdentityMismatch,
    UserNotFound,
    AccountAlreadyExists,
    FlowNotSupported,
    VerificationSessionLost,
    VerificationUserMismatch,
    IdentityHeldByAnotherUser,
    AssuranceLevelInsufficient,
    StaleAuthentication,
    IdentityNotVerified
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalLoginLookup
{
    Identity,
    Email
}

/// <summary>
///     How strongly an identity provider asserts that the person is who they claim to be, in ascending order so a
///     minimum level can be expressed as a comparison. The values mirror the three MitID levels of assurance; MitID
///     Erhverv is deliberately absent, because it returns a different identifier for the same person.
/// </summary>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentityAssuranceLevel
{
    Low,
    Substantial,
    High
}

[PublicAPI]
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalIdentityCapabilities
{
    None = 0,
    Login = 1,
    Verification = 2
}
