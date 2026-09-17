using Account.Features.Authentication.Domain;
using JetBrains.Annotations;
using SharedKernel.Authentication.TokenGeneration;

namespace Account.Features.Authentication.Queries;

// The caller's sessions across the tenants of their email, as GET /api/account/authentication/sessions returns them
[PublicAPI]
public sealed record UserSessionsResponse(UserSessionInfo[] Sessions);

[PublicAPI]
public sealed record UserSessionInfo(
    SessionId Id,
    DateTimeOffset CreatedAt,
    LoginMethod LoginMethod,
    DeviceType DeviceType,
    string UserAgent,
    string IpAddress,
    DateTimeOffset LastActivityAt,
    bool IsCurrent,
    string TenantName
);
