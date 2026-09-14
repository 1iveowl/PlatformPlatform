using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;

namespace Account.Features.Tenants.Queries;

public sealed record GetTenantsForUserQuery : IRequest<Result<GetTenantsForUserResponse>>;

internal sealed class GetTenantsForUserQueryHandler(
    IUserRepository userRepository,
    ITenantRepository tenantRepository,
    IExecutionContext executionContext
) : IRequestHandler<GetTenantsForUserQuery, Result<GetTenantsForUserResponse>>
{
    public async Task<Result<GetTenantsForUserResponse>> Handle(GetTenantsForUserQuery request, CancellationToken cancellationToken)
    {
        // Find all users with the same email across all tenants
        var users = await userRepository.GetUsersByEmailUnfilteredAsync(executionContext.UserInfo.Email!, cancellationToken);
        if (users.Length == 0)
        {
            return Result<GetTenantsForUserResponse>.BadRequest("No tenants found for user.");
        }

        var tenantIds = users.Select(u => u.TenantId).ToArray();
        var tenants = await tenantRepository.GetByIdsAsync(tenantIds, cancellationToken);

        var tenantInfoList = tenants
            .OrderBy(t => t.Id)
            .Select(t =>
                {
                    var user = users.Single(u => u.TenantId == t.Id);
                    return new TenantInfo(t.Id, t.Name, user.Id, t.Logo.Url, !user.EmailConfirmed);
                }
            ).ToArray();

        return new GetTenantsForUserResponse(tenantInfoList);
    }
}
