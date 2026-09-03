using Account.Features.Authentication.Domain;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Shared;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using Account.Features.Users.Shared;
using Account.Integrations.OAuth;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.OpenIdConnect;
using SharedKernel.Telemetry;
using ExternalIdentity = Account.Features.ExternalAuthentication.Domain.ExternalIdentity;

namespace Account.Features.ExternalAuthentication.Commands;

[PublicAPI]
public sealed record CompleteExternalLoginCommand(string? Code, string? State, string? Error, string? ErrorDescription)
    : ICommand, IRequest<Result<string>>
{
    [JsonIgnore]
    public string? Provider { get; init; }
}

public sealed class CompleteExternalLoginHandler(
    IExternalLoginRepository externalLoginRepository,
    IExternalIdentityRepository externalIdentityRepository,
    IUserRepository userRepository,
    ITenantRepository tenantRepository,
    ISessionRepository sessionRepository,
    UserInfoFactory userInfoFactory,
    AuthenticationTokenService authenticationTokenService,
    AvatarUpdater avatarUpdater,
    ExternalAvatarClient externalAvatarClient,
    ExternalAuthenticationHelper externalAuthenticationHelper,
    ExternalAuthenticationService externalAuthenticationService,
    IHttpContextAccessor httpContextAccessor,
    IExecutionContext executionContext,
    ITelemetryEventsCollector events,
    TimeProvider timeProvider,
    ILogger<CompleteExternalLoginHandler> logger
) : IRequestHandler<CompleteExternalLoginCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CompleteExternalLoginCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var validationResult = await externalAuthenticationHelper.ValidateCallback(
                command.Code, command.State, command.Error, command.ErrorDescription, ExternalLoginType.Login, cancellationToken
            );

            if (!validationResult.IsSuccess) return validationResult.ErrorResult!;

            var externalLogin = validationResult.ExternalLogin;
            var externalLoginCookie = validationResult.Cookie;
            var userProfile = validationResult.UserProfile!;

            // Only identities with the Login capability may log in; a verification-only identity is not a match
            var externalIdentities = (await externalIdentityRepository.GetByProviderUserIdUnfilteredAsync(externalLogin.ProviderType, userProfile.ProviderUserId, cancellationToken))
                .Where(ei => ei.Capabilities.HasFlag(ExternalIdentityCapabilities.Login))
                .ToArray();
            var activeUsers = await GetUsersByIdentities(externalIdentities, cancellationToken);
            var lookup = ExternalLoginLookup.Identity;

            if (activeUsers.Length == 0 && userProfile.Email is not null)
            {
                var usersByEmail = await userRepository.GetUsersByEmailUnfilteredAsync(userProfile.Email, cancellationToken);
                activeUsers = await GetUsersInActiveTenants(usersByEmail, cancellationToken);
                lookup = ExternalLoginLookup.Email;
            }

            if (activeUsers.Length == 0)
            {
                logger.LogWarning("No active users found for external login '{ExternalLoginId}'", externalLogin.Id);
                return LoginFailedRedirect(externalLogin, ExternalLoginResult.UserNotFound);
            }

            var user = externalLoginCookie.PreferredTenantId is not null
                ? activeUsers.SingleOrDefault(u => u.TenantId == externalLoginCookie.PreferredTenantId) ?? activeUsers[0]
                : activeUsers[0];

            if (lookup == ExternalLoginLookup.Email)
            {
                var existingIdentity = await externalIdentityRepository.GetByUserIdAndProviderUnfilteredAsync(user.Id, externalLogin.ProviderType, cancellationToken);
                if (existingIdentity is not null && existingIdentity.ProviderUserId != userProfile.ProviderUserId)
                {
                    logger.LogWarning("Identity mismatch for user '{UserId}' with provider '{ProviderType}'", user.Id, externalLogin.ProviderType);
                    return LoginFailedRedirect(externalLogin, ExternalLoginResult.IdentityMismatch);
                }

                if (existingIdentity is null)
                {
                    // The identity lookup matched no live user, so a row for this identity in the user's tenant belongs
                    // to a soft-deleted user. It is removed so the re-invited user can take the identity over without
                    // violating the unique index on provider, provider user id and tenant.
                    foreach (var recycledIdentity in externalIdentities.Where(ei => ei.TenantId == user.TenantId))
                    {
                        externalIdentityRepository.Remove(recycledIdentity);
                    }

                    var externalIdentity = ExternalIdentity.Create(user.TenantId, user.Id, externalLogin.ProviderType, userProfile.ProviderUserId, userProfile.Issuer, userProfile.Subject);
                    await externalIdentityRepository.AddAsync(externalIdentity, cancellationToken);
                }
            }

            // The provider only vouches for its own email, so a stored email is confirmed when it is the one the
            // provider verified; a profile without an email or with a different email leaves it unconfirmed
            if (!user.EmailConfirmed && string.Equals(userProfile.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                user.ConfirmEmail();
                userRepository.Update(user);
            }

            if (user.FirstName is null && user.LastName is null && (userProfile.FirstName is not null || userProfile.LastName is not null))
            {
                user.Update(userProfile.FirstName ?? string.Empty, userProfile.LastName ?? string.Empty, user.Title ?? string.Empty);
                userRepository.Update(user);
            }

            if (userProfile.AvatarUrl is not null && user.Avatar.Url is null)
            {
                var externalAvatar = await externalAvatarClient.DownloadAvatarAsync(userProfile.AvatarUrl, cancellationToken);
                if (externalAvatar is not null)
                {
                    await avatarUpdater.UpdateAvatar(user, false, externalAvatar.ContentType, externalAvatar.Stream, cancellationToken);
                }
            }

            externalLogin.MarkCompleted(userProfile.Email);
            externalLoginRepository.Update(externalLogin);

            var httpContext = httpContextAccessor.HttpContext!;
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();
            var loginMethod = ExternalAuthenticationService.GetLoginMethod(externalLogin.ProviderType);
            var ipAddress = executionContext.ClientIpAddress;
            var session = Session.Create(user.TenantId, user.Id, loginMethod, userAgent, ipAddress);
            await sessionRepository.AddAsync(session, cancellationToken);

            user.UpdateLastSeen(timeProvider.GetUtcNow());
            userRepository.Update(user);

            var userInfoResult = await userInfoFactory.CreateUserInfoAsync(user, session.Id, cancellationToken);
            if (!userInfoResult.IsSuccess) return Result<string>.From(userInfoResult);

            authenticationTokenService.CreateAndSetAuthenticationTokens(userInfoResult.Value!, session.Id, session.RefreshTokenJti);

            events.CollectEvent(new SessionCreated(session.Id));
            var loginTimeInSeconds = (int)(timeProvider.GetUtcNow() - externalLogin.CreatedAt).TotalSeconds;
            events.CollectEvent(new ExternalLoginCompleted(user.Id, externalLogin.ProviderType, lookup, loginTimeInSeconds));

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

    private async Task<User[]> GetUsersByIdentities(ExternalIdentity[] externalIdentities, CancellationToken cancellationToken)
    {
        if (externalIdentities.Length == 0) return [];

        var usersByIdentity = await userRepository.GetByIdsUnfilteredAsync(externalIdentities.Select(ei => ei.UserId).ToArray(), cancellationToken);
        return await GetUsersInActiveTenants(usersByIdentity.OrderBy(u => u.Id).ToArray(), cancellationToken);
    }

    private async Task<User[]> GetUsersInActiveTenants(User[] users, CancellationToken cancellationToken)
    {
        if (users.Length == 0) return users;

        var activeTenantIds = (await tenantRepository.GetByIdsAsync(users.Select(u => u.TenantId).Distinct().ToArray(), cancellationToken))
            .Select(t => t.Id).ToHashSet();
        return users.Where(u => activeTenantIds.Contains(u.TenantId)).ToArray();
    }

    private Result<string> LoginFailedRedirect(ExternalLogin externalLogin, ExternalLoginResult loginResult)
    {
        var timeInSeconds = (int)(timeProvider.GetUtcNow() - externalLogin.CreatedAt).TotalSeconds;
        if (!externalLogin.IsConsumed)
        {
            externalLogin.MarkFailed(loginResult);
            externalLoginRepository.Update(externalLogin);
        }

        events.CollectEvent(new ExternalLoginFailed(externalLogin.Id, loginResult, timeInSeconds));

        var oidcError = ExternalAuthenticationService.MapToOidcError(loginResult);
        return Result<string>.Redirect($"/error?error={oidcError}&id={externalLogin.Id}");
    }
}
