using System.Security.Claims;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SharedKernel.Authentication.BackOfficeIdentity;
using SharedKernel.Cqrs;

namespace Account.Features.BackOffice.Queries;

[PublicAPI]
public sealed record GetMeQuery : IRequest<Result<MeResponse>>;

// Reaches into HttpContext.User directly rather than going through IExecutionContext because back-office
// principals (Easy Auth / mock easy auth) carry no UserId, TenantId, or SessionId — IExecutionContext.UserInfo
// is shaped for tenant-scoped account users and would lose the BackOfficeIdentity claim shape we need here.
public sealed class GetMeHandler(IHttpContextAccessor httpContextAccessor, IOptions<BackOfficeHostOptions> options)
    : IRequestHandler<GetMeQuery, Result<MeResponse>>
{
    private readonly BackOfficeHostOptions _options = options.Value;

    public Task<Result<MeResponse>> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        // The route group requires authorization, so HttpContext.User is always an authenticated BackOfficeIdentity here.
        var principal = httpContextAccessor.HttpContext!.User;
        var displayName = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var groups = principal.Claims
            .Where(claim => claim.Type == BackOfficeIdentityDefaults.GroupsClaimType)
            .Select(claim => claim.Value)
            .ToArray();

        // Closed-by-default: when AdminsGroupId is unset (the BackOfficeAdmin policy admits no one),
        // IsAdmin is false. Mirrors BackOfficeAdminAuthorizationHandler so admin status in the UI agrees
        // with what the policy actually grants on protected endpoints.
        var isAdmin = !string.IsNullOrWhiteSpace(_options.AdminsGroupId) &&
                      groups.Contains(_options.AdminsGroupId);

        return Task.FromResult<Result<MeResponse>>(new MeResponse(displayName, email, isAdmin, groups));
    }
}
