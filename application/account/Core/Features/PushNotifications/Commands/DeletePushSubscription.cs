using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Shared;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.Telemetry;

namespace Account.Features.PushNotifications.Commands;

[PublicAPI]
public sealed record DeletePushSubscriptionCommand : ICommand, IRequest<Result>
{
    [JsonIgnore] // Removes from API contract
    public required PushSubscriptionId Id { get; init; }
}

public sealed class DeletePushSubscriptionHandler(
    IPushSubscriptionRepository pushSubscriptionRepository,
    IExecutionContext executionContext,
    IConfiguration configuration,
    ITelemetryEventsCollector events
) : IRequestHandler<DeletePushSubscriptionCommand, Result>
{
    public async Task<Result> Handle(DeletePushSubscriptionCommand command, CancellationToken cancellationToken)
    {
        if (!PushNotificationPolicy.IsEnabled(configuration)) return Result.NotFound("Push notifications are not available.");

        var userInfo = executionContext.UserInfo;
        if (userInfo.Id is null) return Result.Unauthorized("A push subscription must be removed from an authenticated session.");

        // The tenant query filter already hides another tenant's row, so this is a not found rather than a refusal
        var pushSubscription = await pushSubscriptionRepository.GetByIdAsync(command.Id, cancellationToken);
        if (pushSubscription is null) return Result.NotFound($"Push subscription with ID '{command.Id}' not found.");

        // Another user of the same tenant owns it, which the caller is told plainly rather than by a not found
        if (pushSubscription.UserId != userInfo.Id) return Result.Forbidden("A push subscription can only be removed by the user who made it.");

        pushSubscriptionRepository.Remove(pushSubscription);

        events.CollectEvent(new PushSubscriptionDeleted(pushSubscription.Id));

        return Result.Success();
    }
}
