using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.Authentication.Requests;

// Request bodies of the authentication endpoints. Each record keeps the name and JSON shape of the command it is mapped
// to, because the name is the OpenAPI schema name.

[PublicAPI]
public sealed record SwitchTenantCommand(TenantId TenantId);
