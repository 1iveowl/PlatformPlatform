using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.Domain;

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

[PublicAPI]
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalIdentityCapabilities
{
    None = 0,
    Login = 1,
    Verification = 2
}

/// <summary>
///     The client edition a flow was started from, which decides where its callback sends the browser. The set is
///     closed on purpose: a destination is chosen from it on the server, never taken from a URL the client supplies.
/// </summary>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalLoginEdition
{
    React,
    Blazor
}
