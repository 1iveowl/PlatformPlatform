using Account.Features.ExternalAuthentication.Domain;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;

namespace Account.Features.ExternalAuthentication.Queries;

[PublicAPI]
public sealed record GetVerificationStatusQuery : IRequest<Result<VerificationStatusResponse>>;

/// <summary>
///     Carries no provider user id. A MitID Person-ID identifies a real person, and nothing needs it to render the
///     verification state.
/// </summary>
[PublicAPI]
public sealed record VerificationStatusResponse(bool IsVerified, ExternalProviderType? Provider, IdentityAssuranceLevel? AssuranceLevel, DateTimeOffset? VerifiedAt, DateTimeOffset? AuthenticatedAt);

public sealed class GetVerificationStatusHandler(IExternalIdentityRepository externalIdentityRepository, IExecutionContext executionContext)
    : IRequestHandler<GetVerificationStatusQuery, Result<VerificationStatusResponse>>
{
    public async Task<Result<VerificationStatusResponse>> Handle(GetVerificationStatusQuery query, CancellationToken cancellationToken)
    {
        var userId = executionContext.UserInfo.Id;
        if (userId is null)
        {
            return Result<VerificationStatusResponse>.Unauthorized("The verification status is only available to an authenticated user.");
        }

        var externalIdentities = await externalIdentityRepository.GetByUserIdUnfilteredAsync(userId, cancellationToken);

        // Capabilities are stored as a flag string, so the filter belongs here rather than in the query
        var verifiedIdentity = externalIdentities
            .Where(ei => ei.Capabilities.HasFlag(ExternalIdentityCapabilities.Verification))
            .MaxBy(ei => ei.VerifiedAt);

        if (verifiedIdentity is null)
        {
            return new VerificationStatusResponse(false, null, null, null, null);
        }

        return new VerificationStatusResponse(true, verifiedIdentity.Provider, verifiedIdentity.AssuranceLevel, verifiedIdentity.VerifiedAt, verifiedIdentity.AuthenticatedAt);
    }
}
