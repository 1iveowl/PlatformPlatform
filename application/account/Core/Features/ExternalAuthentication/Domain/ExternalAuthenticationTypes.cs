using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.Domain;

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalProviderType
{
    Google,
    Entra
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalLoginType
{
    Login,
    Signup
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
    AccountAlreadyExists
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
