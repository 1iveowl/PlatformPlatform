using Account.Features.ExternalAuthentication.Domain;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.Domain;
using SharedKernel.Telemetry;

namespace Account.Features.ExternalAuthentication.BackOffice.Commands;

[PublicAPI]
public sealed record RevokeExternalVerificationCommand : ICommand, IRequest<Result>
{
    [JsonIgnore] // Removes from API contract
    public UserId Id { get; init; } = null!;
}

/// <summary>
///     Clears a user's identity verification evidence. A verification callback refuses a provider identity that
///     differs from the one already bound, so a person who verified with the wrong identity, on a shared device or
///     with a family member's, cannot resolve it themselves. This is deliberately administrator-mediated rather than
///     self-service: allowing anyone to move their own binding would defeat the point of binding it.
///     Revoking must leave the user able to verify with a *different* identity, which is the whole point, so a row
///     left with no capabilities is removed rather than kept.
/// </summary>
public sealed class RevokeExternalVerificationHandler(IExternalIdentityRepository externalIdentityRepository, ITelemetryEventsCollector events)
    : IRequestHandler<RevokeExternalVerificationCommand, Result>
{
    public async Task<Result> Handle(RevokeExternalVerificationCommand command, CancellationToken cancellationToken)
    {
        var externalIdentities = await externalIdentityRepository.GetByUserIdUnfilteredAsync(command.Id, cancellationToken);
        var verifiedIdentities = externalIdentities.Where(ei => ei.Capabilities.HasFlag(ExternalIdentityCapabilities.Verification)).ToArray();

        if (verifiedIdentities.Length == 0)
        {
            return Result.NotFound($"User with ID '{command.Id}' has no verified identity.");
        }

        foreach (var externalIdentity in verifiedIdentities)
        {
            externalIdentity.RevokeVerification();

            // A row with no capabilities left links a provider identity to a user for no reason, and it still holds
            // the unique index on user and provider and on provider, provider user id and tenant. Keeping it would
            // make the next verification with a different identity fail as a mismatch, which is the very lock-out
            // this command exists to clear. A row that can still log in is kept, because that capability is in use.
            if (externalIdentity.Capabilities == ExternalIdentityCapabilities.None)
            {
                externalIdentityRepository.Remove(externalIdentity);
            }
            else
            {
                externalIdentityRepository.Update(externalIdentity);
            }

            events.CollectEvent(new ExternalVerificationRevoked(command.Id, externalIdentity.Provider));
        }

        return Result.Success();
    }
}
