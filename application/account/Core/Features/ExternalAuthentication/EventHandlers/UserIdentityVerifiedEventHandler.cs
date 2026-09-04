using Account.Features.ExternalAuthentication.Domain;

namespace Account.Features.ExternalAuthentication.EventHandlers;

public sealed class UserIdentityVerifiedEventHandler(ILogger<UserIdentityVerifiedEventHandler> logger)
    : INotificationHandler<UserIdentityVerifiedEvent>
{
    public Task Handle(UserIdentityVerifiedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Identity verified for user '{UserId}' with provider '{Provider}' at assurance level '{AssuranceLevel}'",
            notification.UserId, notification.Provider, notification.AssuranceLevel
        );

        return Task.CompletedTask;
    }
}
