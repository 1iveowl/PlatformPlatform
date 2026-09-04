using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.Telemetry;

namespace Account.Features.ExternalAuthentication.Commands;

[PublicAPI]
public sealed record StartExternalVerificationCommand(string? ReturnPath = null) : ICommand, IRequest<Result<StartExternalVerificationResponse>>
{
    [JsonIgnore] // Removes from API contract
    public ExternalProviderType ProviderType { get; init; }
}

[PublicAPI]
public sealed record StartExternalVerificationResponse(string AuthorizationUrl);

/// <summary>
///     Starts an identity verification for the signed-in user. This is a command rather than a redirect endpoint on
///     purpose: a GET that requires authorization offers no protection against cross-site request forgery here,
///     because the gateway mints an access token from the SameSite=Lax refresh cookie, so a cross-site navigation
///     would let a third party push an unsolicited identity provider prompt at a person of their choosing.
/// </summary>
public sealed class StartExternalVerificationHandler(
    IExternalLoginRepository externalLoginRepository,
    OAuthProviderFactory oauthProviderFactory,
    ExternalAuthenticationService externalAuthenticationService,
    IHttpContextAccessor httpContextAccessor,
    IExecutionContext executionContext,
    ITelemetryEventsCollector events
) : IRequestHandler<StartExternalVerificationCommand, Result<StartExternalVerificationResponse>>
{
    public async Task<Result<StartExternalVerificationResponse>> Handle(StartExternalVerificationCommand command, CancellationToken cancellationToken)
    {
        var userInfo = executionContext.UserInfo;
        if (userInfo.Id is null || userInfo.TenantId is null)
        {
            return Result<StartExternalVerificationResponse>.Unauthorized("An identity verification must be started from an authenticated session.");
        }

        var actor = new ExternalLoginActor(userInfo.Id, userInfo.TenantId, userInfo.SessionId);

        var result = await StartExternalAuthenticationHelper.StartFlow(
            command.ProviderType, ExternalLoginType.Verification, null, actor, command.ReturnPath,
            externalLoginRepository, oauthProviderFactory, externalAuthenticationService, httpContextAccessor, events, cancellationToken
        );

        if (!result.IsSuccess) return Result<StartExternalVerificationResponse>.From(result);

        return new StartExternalVerificationResponse(result.Value!);
    }
}
