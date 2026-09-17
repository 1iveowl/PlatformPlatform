using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.Requests;

// Request bodies of the external authentication endpoints. Each record keeps the name and JSON shape of the command it is
// mapped to, because the name is the OpenAPI schema name. The provider travels in the route and the edition the flow
// returns to in the query string, not in the body.

[PublicAPI]
public sealed record StartExternalVerificationCommand(string? ReturnPath = null);
