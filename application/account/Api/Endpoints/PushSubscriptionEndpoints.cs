using Account.Features.PushNotifications.Commands;
using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Queries;
using SharedKernel.ApiResults;
using SharedKernel.Endpoints;
using SharedKernel.OpenApi;

namespace Account.Api.Endpoints;

public sealed class PushSubscriptionEndpoints : IEndpoints
{
    private const string RoutesPrefix = "/api/account/users/me/push-subscriptions";

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(RoutesPrefix).WithTags("PushSubscriptions").WithGroupName(OpenApiDocumentNames.Account).RequireAuthorization().ProducesValidationProblem();

        group.MapGet("/", async Task<ApiResult<PushSubscriptionsResponse>> ([AsParameters] GetPushSubscriptionsQuery query, IMediator mediator)
            => await mediator.Send(query)
        ).Produces<PushSubscriptionsResponse>();

        group.MapPost("/", async Task<ApiResult<SavePushSubscriptionResponse>> (SavePushSubscriptionCommand command, IMediator mediator)
            => await mediator.Send(command)
        ).Produces<SavePushSubscriptionResponse>();

        group.MapPost("/test", async Task<ApiResult<SendTestPushNotificationResponse>> (IMediator mediator)
            => await mediator.Send(new SendTestPushNotificationCommand())
        ).Produces<SendTestPushNotificationResponse>();

        group.MapDelete("/{id}", async Task<ApiResult> (PushSubscriptionId id, IMediator mediator)
            => await mediator.Send(new DeletePushSubscriptionCommand { Id = id })
        );
    }
}
