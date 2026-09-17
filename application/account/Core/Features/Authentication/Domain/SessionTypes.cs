using JetBrains.Annotations;
using SharedKernel.Authentication;

namespace Account.Features.Authentication.Domain;

/// <summary>
///     Represents why a session was revoked. This is a domain concept stored in the Session aggregate.
///     For HTTP header reasons (which include additional cases like SessionNotFound), see
///     <see cref="UnauthorizedReason" />.
/// </summary>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionRevokedReason
{
    LoggedOut,
    Revoked,
    ReplayAttackDetected,
    SwitchTenant
}
