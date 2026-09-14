using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.EmailAuthentication.Requests;

// Request bodies of the email authentication endpoints. Each record keeps the name and JSON shape of the command it is
// mapped to, because the name is the OpenAPI schema name. The email login id travels in the route, not in the body.

[PublicAPI]
public sealed record StartEmailLoginCommand(string Email);

[PublicAPI]
public sealed record CompleteEmailLoginCommand(string OneTimePassword, TenantId? PreferredTenantId = null);

[PublicAPI]
public sealed record StartEmailSignupCommand(string Email);

[PublicAPI]
public sealed record CompleteEmailSignupCommand(string OneTimePassword, string PreferredLocale);
