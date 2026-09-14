using Account.Features.EmailAuthentication.Commands;
using Account.Features.EmailAuthentication.Domain;
using SharedKernel.ApiResults;
using SharedKernel.Endpoints;
using SharedKernel.OpenApi;
using EmailAuthenticationRequests = Account.Features.EmailAuthentication.Requests;

namespace Account.Api.Endpoints;

public sealed class EmailAuthenticationEndpoints : IEndpoints
{
    private const string RoutesPrefix = "/api/account/authentication/email";

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(RoutesPrefix).WithTags("EmailAuthentication").WithGroupName(OpenApiDocumentNames.Account).RequireAuthorization().ProducesValidationProblem();

        group.MapPost("/login/start", async Task<ApiResult<StartEmailLoginResponse>> (EmailAuthenticationRequests.StartEmailLoginCommand command, IMediator mediator)
            => await mediator.Send(new StartEmailLoginCommand(command.Email))
        ).Produces<StartEmailLoginResponse>().AllowAnonymous();

        group.MapPost("/login/{id}/complete", async Task<ApiResult> (EmailLoginId id, EmailAuthenticationRequests.CompleteEmailLoginCommand command, IMediator mediator)
            => await mediator.Send(new CompleteEmailLoginCommand(command.OneTimePassword, command.PreferredTenantId) { Id = id })
        ).AllowAnonymous();

        group.MapPost("/login/{id}/resend-code", async Task<ApiResult<ResendEmailLoginCodeResponse>> (EmailLoginId id, IMediator mediator)
            => await mediator.Send(new ResendEmailLoginCodeCommand { Id = id })
        ).Produces<ResendEmailLoginCodeResponse>().AllowAnonymous();

        group.MapPost("/signup/start", async Task<ApiResult<StartEmailSignupResponse>> (EmailAuthenticationRequests.StartEmailSignupCommand command, IMediator mediator)
            => await mediator.Send(new StartEmailSignupCommand(command.Email))
        ).Produces<StartEmailSignupResponse>().AllowAnonymous();

        group.MapPost("/signup/{id}/complete", async Task<ApiResult> (EmailLoginId id, EmailAuthenticationRequests.CompleteEmailSignupCommand command, IMediator mediator)
            => await mediator.Send(new CompleteEmailSignupCommand(command.OneTimePassword, command.PreferredLocale) { EmailLoginId = id })
        ).AllowAnonymous();

        group.MapPost("/signup/{id}/resend-code", async Task<ApiResult<ResendEmailLoginCodeResponse>> (EmailLoginId id, IMediator mediator)
            => await mediator.Send(new ResendEmailLoginCodeCommand { Id = id })
        ).Produces<ResendEmailLoginCodeResponse>().AllowAnonymous();
    }
}
