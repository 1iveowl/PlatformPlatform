using Account.Features.Tenants.Commands;
using Account.Features.Tenants.Queries;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using SharedKernel.Endpoints;
using SharedKernel.OpenApi;
using TenantRequests = Account.Features.Tenants.Requests;

namespace Account.Api.Endpoints;

public sealed class TenantEndpoints : IEndpoints
{
    private const string RoutesPrefix = "/api/account/tenants";

    // Room for the multipart boundaries and part headers around the file, so the request limit never cuts a valid upload
    private const int MultipartOverheadInBytes = 64 * 1024;

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(RoutesPrefix).WithTags("Tenants").WithGroupName(OpenApiDocumentNames.Account).RequireAuthorization().ProducesValidationProblem();

        group.MapGet("/current", async Task<ApiResult<TenantResponse>> (IMediator mediator)
            => await mediator.Send(new GetCurrentTenantQuery())
        ).Produces<TenantResponse>();

        group.MapPut("/current", async Task<ApiResult> (TenantRequests.UpdateCurrentTenantCommand command, IMediator mediator)
            => (await mediator.Send(new UpdateCurrentTenantCommand { Name = command.Name })).AddRefreshAuthenticationTokens()
        );

        group.MapGet("/", async Task<ApiResult<GetTenantsForUserResponse>> (IMediator mediator)
            => await mediator.Send(new GetTenantsForUserQuery())
        ).Produces<GetTenantsForUserResponse>();

        group.MapPost("/current/update-logo", async Task<ApiResult> (IFormFile file, IMediator mediator)
            => await mediator.Send(new UpdateTenantLogoCommand(file.OpenReadStream(), file.ContentType))
        ).WithFormOptions(multipartBodyLengthLimit: UpdateTenantLogoCommand.MaximumFileSizeInBytes).WithMetadata(new RequestSizeLimitAttribute(UpdateTenantLogoCommand.MaximumFileSizeInBytes + MultipartOverheadInBytes));

        group.MapDelete("/current/remove-logo", async Task<ApiResult> (IMediator mediator)
            => await mediator.Send(new RemoveTenantLogoCommand())
        );

        // Internal-only endpoint reachable backend-to-backend via the cluster's localhost address.
        routes.MapDelete("/internal-api/account/tenants/{id}", async Task<ApiResult> (TenantId id, IMediator mediator)
            => await mediator.Send(new DeleteTenantCommand(id))
        ).WithGroupName(OpenApiDocumentNames.Account);
    }
}
