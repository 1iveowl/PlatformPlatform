using Account.Features.Tenants.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.Tenants.Queries;

[PublicAPI]
public sealed record TenantResponse(
    TenantId Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Name,
    TenantState State,
    SuspensionReason? SuspensionReason,
    string? LogoUrl
);

public sealed record GetTenantsForUserResponse(TenantInfo[] Tenants);

public sealed record TenantInfo(TenantId TenantId, string? TenantName, UserId UserId, string? LogoUrl, bool IsNew);
