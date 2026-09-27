using JetBrains.Annotations;
using SharedKernel.FeatureFlags;

namespace Account.Features.BackOffice.Requests;

// Requests of the back-office endpoints. Each record keeps the name and JSON shape of the command it is mapped to. The tenant
// id of SetTenantAbInclusionPinCommand travels in the route, not in the body; a null pin clears it.
[PublicAPI]
public sealed record SetTenantAbInclusionPinCommand(AbInclusionPin? AbInclusionPin);
