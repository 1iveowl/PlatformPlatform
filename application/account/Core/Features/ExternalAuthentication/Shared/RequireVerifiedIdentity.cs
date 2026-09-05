using Account.Features.ExternalAuthentication.Domain;

namespace Account.Features.ExternalAuthentication.Shared;

/// <summary>
///     The enforcement point for a handler that needs the person behind an account to have proved who they are.
///     Nothing calls it yet, because nothing in the product requires a verified identity today. It exists so the
///     freshness rule is written down once, while the semantics are in hand: freshness is measured against when the
///     identity provider says the person authenticated, not against when the row was written. Those differ whenever a
///     provider replays a cached session, and getting it wrong would make a years-old authentication look current.
/// </summary>
public static class RequireVerifiedIdentity
{
    public static bool IsSatisfied(ExternalIdentity? externalIdentity, IdentityAssuranceLevel minimumAssuranceLevel, TimeSpan maximumAge, DateTimeOffset now)
    {
        if (externalIdentity is null) return false;

        if (!externalIdentity.Capabilities.HasFlag(ExternalIdentityCapabilities.Verification)) return false;

        if (externalIdentity.AssuranceLevel is null || externalIdentity.AssuranceLevel < minimumAssuranceLevel) return false;

        if (externalIdentity.AuthenticatedAt is null) return false;

        return now - externalIdentity.AuthenticatedAt.Value <= maximumAge;
    }
}
