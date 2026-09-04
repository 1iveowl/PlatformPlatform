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
using SharedKernel.Domain;
using SharedKernel.ExecutionContext;
using SharedKernel.OpenIdConnect;
using SharedKernel.Telemetry;
using ExternalIdentity = Account.Features.ExternalAuthentication.Domain.ExternalIdentity;

namespace Account.Features.ExternalAuthentication.Commands;

[PublicAPI]
public sealed record CompleteExternalLoginCommand(string? Code, string? State, string? Error, string? ErrorDescription)
    : ICommand, IRequest<Result<string>>
{
    [JsonIgnore] // Removes from API contract
    public ExternalProviderType ProviderType { get; init; }
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
                command.Code, command.State, command.Error, command.ErrorDescription, command.ProviderType, ExternalLoginType.Login, cancellationToken
            );

            if (!validationResult.IsSuccess) return validationResult.ErrorResult!;

            var externalLogin = validationResult.ExternalLogin;
            var externalLoginCookie = validationResult.Cookie;
            var userProfile = validationResult.UserProfile!;

            var identitiesForProviderUserId = await externalIdentityRepository.GetByProviderUserIdUnfilteredAsync(externalLogin.ProviderType, userProfile.ProviderUserId, cancellationToken);

            // Only identities with the Login capability may log in; a verification-only identity is not a match. The
            // unfiltered array is kept as well, because a row that cannot log in still occupies the unique index on
            // provider, provider user id and tenant.
            var loginCapableIdentities = identitiesForProviderUserId.Where(ei => ei.Capabilities.HasFlag(ExternalIdentityCapabilities.Login)).ToArray();
            var identityCandidates = await GetUsersByIdentities(loginCapableIdentities, cancellationToken);

            // The email candidates are loaded alongside the identity candidates instead of only when the identity
            // lookup came up empty. A person invited by email to a second tenant has no identity row there, and
            // without this the preferred tenant could never be honoured and they would land in the other tenant.
            var emailCandidates = userProfile.Email is null
                ? []
                : await GetUsersInActiveTenants(await userRepository.GetUsersByEmailUnfilteredAsync(userProfile.Email, cancellationToken), cancellationToken);

            var (user, lookup) = SelectUser(identityCandidates, emailCandidates, externalLoginCookie.PreferredTenantId);

            if (user is null)
            {
                logger.LogWarning("No active users found for external login '{ExternalLoginId}'", externalLogin.Id);
                return LoginFailedRedirect(externalLogin, ExternalLoginResult.UserNotFound);
            }

            if (lookup == ExternalLoginLookup.Email)
            {
                // The capability is deliberately not part of this lookup: filtering it out here would hide a
                // verification-only row and make the insert below violate the unique index on user and provider.
                var existingIdentity = await externalIdentityRepository.GetByUserIdAndProviderUnfilteredAsync(user.Id, externalLogin.ProviderType, cancellationToken);
                if (existingIdentity is not null && existingIdentity.ProviderUserId != userProfile.ProviderUserId)
                {
                    logger.LogWarning("Identity mismatch for user '{UserId}' with provider '{ProviderType}'", user.Id, externalLogin.ProviderType);
                    return LoginFailedRedirect(externalLogin, ExternalLoginResult.IdentityMismatch);
                }

                if (existingIdentity is null)
                {
                    var conflictResult = await ResolveIdentityHeldByAnotherUser(externalLogin, identitiesForProviderUserId, user.TenantId, cancellationToken);
                    if (conflictResult is not null) return conflictResult;

                    var externalIdentity = ExternalIdentity.Create(user.TenantId, user.Id, externalLogin.ProviderType, userProfile.ProviderUserId, userProfile.Issuer, userProfile.Subject);
                    await externalIdentityRepository.AddAsync(externalIdentity, cancellationToken);
                }
                else if (!existingIdentity.Capabilities.HasFlag(ExternalIdentityCapabilities.Login))
                {
                    // The row already holds this exact identity, and the provider verified an email that matches the
                    // user's own. That is the same evidence the branch above accepts to create a new Login row, so the
                    // verification-only row is upgraded; the unique index leaves no room for a second row anyway.
                    existingIdentity.AddLoginCapability();
                    externalIdentityRepository.Update(existingIdentity);
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

    /// <summary>
    ///     Picks the user to log in and reports which of the two candidate lists it came from. A preferred tenant from
    ///     the login cookie wins, by identity first and by email second, and falls through to the first candidate by
    ///     user id when neither list covers that tenant. Both lists arrive ordered by user id, so the positional
    ///     fall-through below is deterministic.
    /// </summary>
    private static (User? User, ExternalLoginLookup Lookup) SelectUser(User[] identityCandidates, User[] emailCandidates, TenantId? preferredTenantId)
    {
        if (preferredTenantId is not null)
        {
            // The two operators differ because the two lists are guaranteed differently. Two identity candidates in
            // one tenant are impossible: the unique index on provider, provider user id and tenant allows one row
            // per tenant for this identity, and the composite foreign key on tenant id and user id ties that row to
            // a user in the same tenant. SingleOrDefault keeps that as an assertion rather than a comment, so a
            // schema change that drops either half fails visibly instead of silently logging someone into the wrong
            // account in their preferred tenant.
            var preferredIdentityUser = identityCandidates.SingleOrDefault(u => u.TenantId == preferredTenantId);
            if (preferredIdentityUser is not null) return (preferredIdentityUser, ExternalLoginLookup.Identity);

            // The email list needs no such guard: the unique index on tenant id and email, filtered to live users,
            // makes a second candidate in one tenant impossible.
            var preferredEmailUser = emailCandidates.FirstOrDefault(u => u.TenantId == preferredTenantId);
            if (preferredEmailUser is not null) return (preferredEmailUser, ExternalLoginLookup.Email);
        }

        if (identityCandidates.Length > 0) return (identityCandidates[0], ExternalLoginLookup.Identity);
        if (emailCandidates.Length > 0) return (emailCandidates[0], ExternalLoginLookup.Email);

        return (null, ExternalLoginLookup.Identity);
    }

    /// <summary>
    ///     The unique index on provider, provider user id and tenant allows one holder of an identity per tenant, so a
    ///     row already in the chosen user's tenant has to be dealt with before inserting. A row left behind by a
    ///     soft-deleted user is freed, which is how a re-invited person takes their identity back. A row held by a live
    ///     user is refused with a distinct result: it can only be a verification-only row, invisible to the login
    ///     lookup, and inserting anyway would violate the index and surface as a server error. Removing it instead is
    ///     not an option, because that would destroy another person's verification evidence as a side effect of a
    ///     login.
    /// </summary>
    private async Task<Result<string>?> ResolveIdentityHeldByAnotherUser(ExternalLogin externalLogin, ExternalIdentity[] identitiesForProviderUserId, TenantId tenantId, CancellationToken cancellationToken)
    {
        var identitiesInTenant = identitiesForProviderUserId.Where(ei => ei.TenantId == tenantId).ToArray();
        if (identitiesInTenant.Length == 0) return null;

        var liveUserIds = (await userRepository.GetByIdsUnfilteredAsync(identitiesInTenant.Select(ei => ei.UserId).ToArray(), cancellationToken))
            .Select(u => u.Id).ToHashSet();

        if (identitiesInTenant.Any(ei => liveUserIds.Contains(ei.UserId)))
        {
            logger.LogWarning("The '{ProviderType}' identity presented for external login '{ExternalLoginId}' is already held by another user in the tenant", externalLogin.ProviderType, externalLogin.Id);
            return LoginFailedRedirect(externalLogin, ExternalLoginResult.IdentityHeldByAnotherUser);
        }

        foreach (var recycledIdentity in identitiesInTenant)
        {
            externalIdentityRepository.Remove(recycledIdentity);
        }

        return null;
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
