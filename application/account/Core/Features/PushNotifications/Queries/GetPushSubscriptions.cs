using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Shared;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;

namespace Account.Features.PushNotifications.Queries;

[PublicAPI]
public sealed record GetPushSubscriptionsQuery : IRequest<Result<PushSubscriptionsResponse>>;

public sealed class GetPushSubscriptionsHandler(
    IPushSubscriptionRepository pushSubscriptionRepository,
    IExecutionContext executionContext,
    IConfiguration configuration
) : IRequestHandler<GetPushSubscriptionsQuery, Result<PushSubscriptionsResponse>>
{
    public async Task<Result<PushSubscriptionsResponse>> Handle(GetPushSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        if (!PushNotificationPolicy.IsEnabled(configuration)) return Result<PushSubscriptionsResponse>.NotFound("Push notifications are not available.");

        var userInfo = executionContext.UserInfo;
        if (userInfo.Id is null) return Result<PushSubscriptionsResponse>.Unauthorized("Push subscriptions must be read from an authenticated session.");

        // Only the caller's own devices: the tenant query filter scopes the table and the user id narrows it to this user
        var subscriptions = await pushSubscriptionRepository.GetByUserAsync(userInfo.Id, cancellationToken);

        return new PushSubscriptionsResponse(subscriptions.Select(s => new PushSubscriptionDetails(s.Id, s.DeviceLabel, s.CreatedAt)).ToArray());
    }
}
