using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.Commands;

// The authorization URL of the identity provider a verification continues at, a different origin the client navigates the
// whole document to
[PublicAPI]
public sealed record StartExternalVerificationResponse(string AuthorizationUrl);
