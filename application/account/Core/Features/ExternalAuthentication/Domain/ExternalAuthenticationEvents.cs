using SharedKernel.Domain;
using SharedKernel.DomainEvents;

namespace Account.Features.ExternalAuthentication.Domain;

/// <summary>
///     Raised whenever an identity verification succeeds, including a re-verification that refreshes an existing one.
///     It deliberately carries no provider user id: a MitID Person-ID identifies a real person and must not reach a
///     log line, an event or a trace.
/// </summary>
public sealed record UserIdentityVerifiedEvent(TenantId TenantId, UserId UserId, ExternalProviderType Provider, IdentityAssuranceLevel AssuranceLevel) : IDomainEvent;
