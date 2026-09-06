using Account.Features.ExternalAuthentication.Domain;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.Domain;

namespace Account.Features.ExternalAuthentication.BackOffice.Queries;

[PublicAPI]
public sealed record GetBackOfficeUserIdentityVerificationQuery(UserId Id)
    : IRequest<Result<BackOfficeUserIdentityVerificationResponse>>;

/// <summary>
///     Carries no provider user id, for the same reason the self-service verification status does not: a MitID
///     Person-ID identifies a real person, and an administrator needs to know that a verification exists and how
///     strong it is, never the identifier itself. Everything here is evidence about the verification rather than
///     about the person.
/// </summary>
[PublicAPI]
public sealed record BackOfficeUserIdentityVerificationResponse(
    bool IsVerified,
    ExternalProviderType? Provider,
    IdentityAssuranceLevel? AssuranceLevel,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? AuthenticatedAt
);

public sealed class GetBackOfficeUserIdentityVerificationHandler(IExternalIdentityRepository externalIdentityRepository)
    : IRequestHandler<GetBackOfficeUserIdentityVerificationQuery, Result<BackOfficeUserIdentityVerificationResponse>>
{
    public async Task<Result<BackOfficeUserIdentityVerificationResponse>> Handle(GetBackOfficeUserIdentityVerificationQuery query, CancellationToken cancellationToken)
    {
        var externalIdentities = await externalIdentityRepository.GetByUserIdUnfilteredAsync(query.Id, cancellationToken);

        // Capabilities are stored as a flag string, so the filter belongs here rather than in the query. A user can
        // hold several identities and more than one could carry the verification capability, so the most recent one
        // is the one that describes their current state, matching what the person sees on their own profile.
        var verifiedIdentity = externalIdentities
            .Where(ei => ei.Capabilities.HasFlag(ExternalIdentityCapabilities.Verification))
            .MaxBy(ei => ei.VerifiedAt);

        if (verifiedIdentity is null)
        {
            return new BackOfficeUserIdentityVerificationResponse(false, null, null, null, null);
        }

        return new BackOfficeUserIdentityVerificationResponse(
            true,
            verifiedIdentity.Provider,
            verifiedIdentity.AssuranceLevel,
            verifiedIdentity.VerifiedAt,
            verifiedIdentity.AuthenticatedAt
        );
    }
}
