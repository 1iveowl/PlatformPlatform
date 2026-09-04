using Account.Features.ExternalAuthentication.Commands;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Queries;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.ApiResults;
using SharedKernel.Endpoints;
using SharedKernel.OpenApi;

namespace Account.Api.Endpoints;

public sealed class ExternalAuthenticationEndpoints : IEndpoints
{
    private const string RoutesPrefix = "/api/account/authentication";

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(RoutesPrefix).WithTags("ExternalAuthentication").WithGroupName(OpenApiDocumentNames.Account).RequireAuthorization().ProducesValidationProblem();

        group.MapGet("/{provider}/login/start", async Task<ApiResult<string>> (ExternalProviderType provider, [AsParameters] StartExternalLoginCommand command, IMediator mediator)
            => await mediator.Send(command with { ProviderType = provider })
        ).AllowAnonymous();

        group.MapGet("/{provider}/login/callback", async Task<ApiResult<string>> (ExternalProviderType provider, string? code, string? state, string? error, [FromQuery(Name = "error_description")] string? errorDescription, IMediator mediator)
            => await mediator.Send(new CompleteExternalLoginCommand(code, state, error, errorDescription) { ProviderType = provider })
        ).AllowAnonymous();

        group.MapGet("/{provider}/signup/start", async Task<ApiResult<string>> (ExternalProviderType provider, IMediator mediator)
            => await mediator.Send(new StartExternalSignupCommand { ProviderType = provider })
        ).AllowAnonymous();

        group.MapGet("/{provider}/signup/callback", async Task<ApiResult<string>> (ExternalProviderType provider, string? code, string? state, string? error, [FromQuery(Name = "error_description")] string? errorDescription, IMediator mediator)
            => await mediator.Send(new CompleteExternalSignupCommand(code, state, error, errorDescription) { ProviderType = provider })
        ).AllowAnonymous();

        // Deliberately a POST rather than a redirect endpoint: a GET is not protected by the antiforgery middleware,
        // and the gateway mints an access token from the SameSite=Lax refresh cookie, so a cross-site navigation to a
        // GET would let a third party push an unsolicited identity provider prompt at a signed-in person
        group.MapPost("/{provider}/verification/start", async Task<ApiResult<StartExternalVerificationResponse>> (ExternalProviderType provider, StartExternalVerificationCommand command, IMediator mediator)
            => await mediator.Send(command with { ProviderType = provider })
        ).Produces<StartExternalVerificationResponse>();

        // Anonymous because the identity provider redirects the browser back cross-site, where the SameSite=Strict
        // access token cookie is not sent. The flow is bound to its user through the data protected flow cookie
        group.MapGet("/{provider}/verification/callback", async Task<ApiResult<string>> (ExternalProviderType provider, string? code, string? state, string? error, [FromQuery(Name = "error_description")] string? errorDescription, IMediator mediator)
            => await mediator.Send(new CompleteExternalVerificationCommand(code, state, error, errorDescription) { ProviderType = provider })
        ).AllowAnonymous();

        group.MapGet("/verification", async Task<ApiResult<VerificationStatusResponse>> ([AsParameters] GetVerificationStatusQuery query, IMediator mediator)
            => await mediator.Send(query)
        ).Produces<VerificationStatusResponse>();
    }
}
