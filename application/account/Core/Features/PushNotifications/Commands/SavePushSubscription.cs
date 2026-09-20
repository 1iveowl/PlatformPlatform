using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Shared;
using FluentValidation;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.Telemetry;

namespace Account.Features.PushNotifications.Commands;

[PublicAPI]
public sealed record SavePushSubscriptionCommand(string Endpoint, string PublicKey, string AuthSecret, string DeviceLabel, string ApplicationPath)
    : ICommand, IRequest<Result<SavePushSubscriptionResponse>>;

public sealed class SavePushSubscriptionValidator : AbstractValidator<SavePushSubscriptionCommand>
{
    public SavePushSubscriptionValidator()
    {
        RuleFor(x => x.Endpoint).Must(PushNotificationPolicy.IsPushServiceEndpoint).WithMessage("The push service address must be an https address of at most 2000 characters.");
        RuleFor(x => x.PublicKey).Must(PushNotificationPolicy.IsSubscriptionPublicKey).WithMessage("The subscription key must be a base64url encoded uncompressed P-256 public key.");
        RuleFor(x => x.AuthSecret).Must(PushNotificationPolicy.IsSubscriptionAuthSecret).WithMessage("The subscription secret must be a base64url encoded 16 byte value.");
        RuleFor(x => x.DeviceLabel).NotEmpty().MaximumLength(PushNotificationPolicy.MaximumDeviceLabelLength).WithMessage("The device label must be between 1 and 100 characters.");
        RuleFor(x => x.ApplicationPath).Must(PushNotificationPolicy.IsApplicationPath).WithMessage("The application path must be a rooted path of at most 200 characters.");
    }
}

public sealed class SavePushSubscriptionHandler(
    IPushSubscriptionRepository pushSubscriptionRepository,
    IExecutionContext executionContext,
    IConfiguration configuration,
    ITelemetryEventsCollector events
) : IRequestHandler<SavePushSubscriptionCommand, Result<SavePushSubscriptionResponse>>
{
    public async Task<Result<SavePushSubscriptionResponse>> Handle(SavePushSubscriptionCommand command, CancellationToken cancellationToken)
    {
        if (!PushNotificationPolicy.IsEnabled(configuration)) return Result<SavePushSubscriptionResponse>.NotFound("Push notifications are not available.");

        var userInfo = executionContext.UserInfo;
        if (userInfo.Id is null || userInfo.TenantId is null) return Result<SavePushSubscriptionResponse>.Unauthorized("A push subscription must be saved from an authenticated session.");

        // The same browser resubscribing arrives with the same endpoint, so its row is updated rather than duplicated
        var existingSubscription = await pushSubscriptionRepository.GetByEndpointAsync(userInfo.Id, command.Endpoint, cancellationToken);
        if (existingSubscription is not null)
        {
            existingSubscription.Update(command.PublicKey, command.AuthSecret, command.DeviceLabel, command.ApplicationPath);
            pushSubscriptionRepository.Update(existingSubscription);
            events.CollectEvent(new PushSubscriptionUpdated(existingSubscription.Id));
            return new SavePushSubscriptionResponse(existingSubscription.Id);
        }

        var pushSubscription = PushSubscription.Create(userInfo.TenantId, userInfo.Id, command.Endpoint, command.PublicKey, command.AuthSecret, command.DeviceLabel, command.ApplicationPath);
        await pushSubscriptionRepository.AddAsync(pushSubscription, cancellationToken);

        events.CollectEvent(new PushSubscriptionCreated(pushSubscription.Id));

        return new SavePushSubscriptionResponse(pushSubscription.Id);
    }
}
