using Account.Features.ExternalAuthentication.Domain;
using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.Queries;

/// <summary>
///     Carries no provider user id. A MitID Person-ID identifies a real person, and nothing needs it to render the
///     verification state.
/// </summary>
[PublicAPI]
public sealed record VerificationStatusResponse(bool IsVerified, ExternalProviderType? Provider, IdentityAssuranceLevel? AssuranceLevel, DateTimeOffset? VerifiedAt, DateTimeOffset? AuthenticatedAt);
