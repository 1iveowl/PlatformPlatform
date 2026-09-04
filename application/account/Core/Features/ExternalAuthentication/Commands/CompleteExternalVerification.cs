using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Shared;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using SharedKernel.Cqrs;
using SharedKernel.Domain;
using SharedKernel.OpenIdConnect;
using SharedKernel.Telemetry;
using ExternalIdentity = Account.Features.ExternalAuthentication.Domain.ExternalIdentity;

namespace Account.Features.ExternalAuthentication.Commands;

[PublicAPI]
public sealed record CompleteExternalVerificationCommand(string? Code, string? State, string? Error, string? ErrorDescription)
    : ICommand, IRequest<Result<string>>
{
    [JsonIgnore] // Removes from API contract
    public ExternalProviderType ProviderType { get; init; }
}

/// <summary>
///     The only writer of verification evidence. It resolves no account and creates no session: the user is already
///     signed in and was bound to the flow when it started, so all this does is attach the provider identity to that
///     account and record how and when it was proved.
/// </summary>
public sealed class CompleteExternalVerificationHandler(
    IExternalLoginRepository externalLoginRepository,
    IExternalIdentityRepository externalIdentityRepository,
    IUserRepository userRepository,
    ExternalAuthenticationHelper externalAuthenticationHelper,
    ExternalAuthenticationService externalAuthenticationService,
    IHttpContextAccessor httpContextAccessor,
    ITelemetryEventsCollector events,
    TimeProvider timeProvider,
    ILogger<CompleteExternalVerificationHandler> logger
) : IRequestHandler<CompleteExternalVerificationCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CompleteExternalVerificationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var validationResult = await externalAuthenticationHelper.ValidateCallback(
                command.Code, command.State, command.Error, command.ErrorDescription, command.ProviderType, ExternalLoginType.Verification, cancellationToken
            );

            if (!validationResult.IsSuccess) return validationResult.ErrorResult!;

            var externalLogin = validationResult.ExternalLogin;
            var userProfile = validationResult.UserProfile!;

            // Guaranteed by ExternalLogin.Create and re-checked against the flow cookie and the session in ValidateCallback
            var userId = externalLogin.UserId!;
            var tenantId = externalLogin.TenantId!;

            if (userProfile.AssuranceLevel is null || userProfile.AuthenticationInstant is null)
            {
                logger.LogWarning("Provider '{ProviderType}' returned no assurance level or authentication time for external login '{ExternalLoginId}'", externalLogin.ProviderType, externalLogin.Id);
                return VerificationFailedRedirect(externalLogin, ExternalLoginResult.AssuranceLevelInsufficient);
            }

            var existingIdentity = await externalIdentityRepository.GetByUserIdAndProviderUnfilteredAsync(userId, externalLogin.ProviderType, cancellationToken);

            if (existingIdentity is not null && existingIdentity.ProviderUserId != userProfile.ProviderUserId)
            {
                // Re-binding an account to a different person is exactly what verification exists to prevent, so this
                // is refused. A back office administrator can revoke the binding when someone verified with the wrong
                // identity, for example on a shared device.
                logger.LogWarning("User '{UserId}' is already verified with another '{ProviderType}' identity", userId, externalLogin.ProviderType);
                return VerificationFailedRedirect(externalLogin, ExternalLoginResult.IdentityMismatch);
            }

            if (existingIdentity is null)
            {
                var conflictResult = await ResolveIdentityHeldByAnotherUser(externalLogin, userProfile.ProviderUserId, tenantId, cancellationToken);
                if (conflictResult is not null) return conflictResult;

                var externalIdentity = ExternalIdentity.CreateForVerification(
                    tenantId, userId, externalLogin.ProviderType, userProfile.ProviderUserId, userProfile.Issuer, userProfile.Subject,
                    userProfile.AssuranceLevel.Value, timeProvider.GetUtcNow(), userProfile.AuthenticationInstant.Value, externalLogin.Id
                );
                await externalIdentityRepository.AddAsync(externalIdentity, cancellationToken);
            }
            else
            {
                // Same person, same identity: re-verifying is how a verification is kept fresh
                existingIdentity.RecordVerification(userProfile.AssuranceLevel.Value, timeProvider.GetUtcNow(), userProfile.AuthenticationInstant.Value, externalLogin.Id);
                externalIdentityRepository.Update(existingIdentity);
            }

            // No email is stored: a MitID profile carries none, and nothing about the person is recorded beyond the
            // identifier and the evidence
            externalLogin.MarkCompleted(null);
            externalLoginRepository.Update(externalLogin);

            var verificationTimeInSeconds = (int)(timeProvider.GetUtcNow() - externalLogin.CreatedAt).TotalSeconds;
            events.CollectEvent(new ExternalVerificationCompleted(userId, externalLogin.ProviderType, userProfile.AssuranceLevel.Value, verificationTimeInSeconds));

            var httpContext = httpContextAccessor.HttpContext!;
            var returnPath = ReturnPathHelper.GetReturnPathCookie(httpContext) ?? "/";
            ReturnPathHelper.ClearReturnPathCookie(httpContext);

            return Result<string>.Redirect(returnPath);
        }
        finally
        {
            externalAuthenticationService.ClearExternalLoginCookie();
            externalAuthenticationService.ClearLocaleCookie();
        }
    }

    /// <summary>
    ///     The unique index on provider, provider user id and tenant allows one holder of an identity per tenant. A row
    ///     left behind by a soft-deleted user is freed, because a person who returns to a tenant must be able to verify
    ///     again; a row held by a live user is refused with a distinct result, because inserting would otherwise fail
    ///     in the database and surface as a server error.
    /// </summary>
    private async Task<Result<string>?> ResolveIdentityHeldByAnotherUser(ExternalLogin externalLogin, string providerUserId, TenantId tenantId, CancellationToken cancellationToken)
    {
        var identitiesInTenant = (await externalIdentityRepository.GetByProviderUserIdUnfilteredAsync(externalLogin.ProviderType, providerUserId, cancellationToken))
            .Where(ei => ei.TenantId == tenantId)
            .ToArray();

        if (identitiesInTenant.Length == 0) return null;

        var liveUserIds = (await userRepository.GetByIdsUnfilteredAsync(identitiesInTenant.Select(ei => ei.UserId).ToArray(), cancellationToken))
            .Select(u => u.Id).ToHashSet();

        if (identitiesInTenant.Any(ei => liveUserIds.Contains(ei.UserId)))
        {
            logger.LogWarning("The '{ProviderType}' identity presented for external login '{ExternalLoginId}' is already held by another user in the tenant", externalLogin.ProviderType, externalLogin.Id);
            return VerificationFailedRedirect(externalLogin, ExternalLoginResult.IdentityHeldByAnotherUser);
        }

        foreach (var recycledIdentity in identitiesInTenant)
        {
            externalIdentityRepository.Remove(recycledIdentity);
        }

        return null;
    }

    private Result<string> VerificationFailedRedirect(ExternalLogin externalLogin, ExternalLoginResult loginResult)
    {
        var timeInSeconds = (int)(timeProvider.GetUtcNow() - externalLogin.CreatedAt).TotalSeconds;
        if (!externalLogin.IsConsumed)
        {
            externalLogin.MarkFailed(loginResult);
            externalLoginRepository.Update(externalLogin);
        }

        events.CollectEvent(new ExternalVerificationFailed(externalLogin.Id, loginResult, timeInSeconds));

        var oidcError = ExternalAuthenticationService.MapToOidcError(loginResult);
        return Result<string>.Redirect($"/error?error={oidcError}&id={externalLogin.Id}");
    }
}
