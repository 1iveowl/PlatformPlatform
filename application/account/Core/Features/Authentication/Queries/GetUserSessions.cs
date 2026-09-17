using Account.Features.Authentication.Domain;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;

namespace Account.Features.Authentication.Queries;

[PublicAPI]
public sealed record GetUserSessionsQuery : IRequest<Result<UserSessionsResponse>>;

public sealed class GetUserSessionsHandler(
    ISessionRepository sessionRepository,
    IUserRepository userRepository,
    ITenantRepository tenantRepository,
    IExecutionContext executionContext
) : IRequestHandler<GetUserSessionsQuery, Result<UserSessionsResponse>>
{
    public async Task<Result<UserSessionsResponse>> Handle(GetUserSessionsQuery query, CancellationToken cancellationToken)
    {
        var userEmail = executionContext.UserInfo.Email!;
        var currentSessionId = executionContext.UserInfo.SessionId;

        var users = await userRepository.GetUsersByEmailUnfilteredAsync(userEmail, cancellationToken);
        var userIds = users.Select(u => u.Id).ToArray();

        var sessions = await sessionRepository.GetActiveSessionsForUsersUnfilteredAsync(userIds, cancellationToken);

        var tenantIds = sessions.Select(s => s.TenantId).Distinct().ToArray();
        var tenants = await tenantRepository.GetByIdsAsync(tenantIds, cancellationToken);
        var tenantLookup = tenants.ToDictionary(t => t.Id, t => t.Name);

        var sessionInfos = sessions.Select(s => new UserSessionInfo(
                s.Id,
                s.CreatedAt,
                s.LoginMethod,
                s.DeviceType,
                s.UserAgent,
                s.IpAddress,
                s.ModifiedAt ?? s.CreatedAt,
                currentSessionId is not null && s.Id == currentSessionId,
                tenantLookup.GetValueOrDefault(s.TenantId) ?? string.Empty
            )
        ).ToArray();

        return new UserSessionsResponse(sessionInfos);
    }
}
