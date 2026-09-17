using JetBrains.Annotations;

namespace Account.Features.Tenants.Requests;

// Request bodies of the tenant endpoints. Each record keeps the name and JSON shape of the command it is mapped to,
// because the name is the OpenAPI schema name.

[PublicAPI]
public sealed record UpdateCurrentTenantCommand
{
    public required string Name { get; init; }
}

// The logo image the update-logo endpoint binds as the multipart form file "file", with the limits its validator
// enforces. The client sends the stream as the file and disposes it with the request.
[PublicAPI]
public sealed record UpdateTenantLogoCommand(Stream FileStream, string ContentType)
{
    public const long MaximumFileSizeInBytes = 2 * 1024 * 1024;

    public static readonly IReadOnlyList<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];
}
