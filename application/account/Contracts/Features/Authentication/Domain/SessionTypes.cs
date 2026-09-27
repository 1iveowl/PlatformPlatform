using JetBrains.Annotations;

namespace Account.Features.Authentication.Domain;

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceType
{
    Unknown,
    Desktop,
    Mobile,
    Tablet
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoginMethod
{
    OneTimePassword,
    Google,
    Entra,
    MitId
}

// Why a session was revoked, as the Session aggregate stores it. The reasons an unauthorized response names in its header,
// which add cases such as SessionNotFound, are UnauthorizedReason in the shared kernel.
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionRevokedReason
{
    LoggedOut,
    Revoked,
    ReplayAttackDetected,
    SwitchTenant
}
