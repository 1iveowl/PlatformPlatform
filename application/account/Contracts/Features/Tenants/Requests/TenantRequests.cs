using JetBrains.Annotations;

namespace Account.Features.Tenants.Requests;

// Request bodies of the tenant endpoints. Each record keeps the name and JSON shape of the command it is mapped to,
// because the name is the OpenAPI schema name.

[PublicAPI]
public sealed record UpdateCurrentTenantCommand
{
    public required string Name { get; init; }
}
