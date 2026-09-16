using Account.Features.Authentication.Queries;
using SharedKernel.ApiResults;
using SharedKernel.Endpoints;
using SharedKernel.OpenApi;

namespace Account.Api.Endpoints;

public sealed class BootstrapEndpoints : IEndpoints
{
    private const string RoutesPrefix = "/api/account/bootstrap";

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(RoutesPrefix).WithTags("Bootstrap").WithGroupName(OpenApiDocumentNames.Account).RequireAuthorization().ProducesValidationProblem();

        group.MapGet("/", async Task<ApiResult<BootstrapResponse>> ([AsParameters] GetBootstrapQuery query, IMediator mediator)
            => await mediator.Send(query)
        ).Produces<BootstrapResponse>().AllowAnonymous();
    }
}
