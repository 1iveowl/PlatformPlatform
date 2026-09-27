using Account.Features.ExternalAuthentication.Domain;
using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.BackOffice.Queries;

/// <summary>
///     A user's identity verification as GET /api/back-office/users/{id}/identity-verification returns it. Carries no provider
///     user id, for the same reason the self-service verification status does not: a MitID Person-ID identifies a real person,
///     and an administrator needs to know that a verification exists and how strong it is, never the identifier itself.
///     Everything here is evidence about the verification rather than about the person. The query record stays with its
///     handler in the account API.
/// </summary>
[PublicAPI]
public sealed record BackOfficeUserIdentityVerificationResponse(
    bool IsVerified,
    ExternalProviderType? Provider,
    IdentityAssuranceLevel? AssuranceLevel,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? AuthenticatedAt
);
