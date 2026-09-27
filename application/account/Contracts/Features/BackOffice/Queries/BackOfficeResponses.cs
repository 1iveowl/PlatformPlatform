using JetBrains.Annotations;

namespace Account.Features.BackOffice.Queries;

// The signed-in back-office identity as GET /api/back-office/me returns it. IsAdmin is the account API's own reading of the
// admins group claim, the one its BackOfficeAdmin policy enforces.
[PublicAPI]
public sealed record MeResponse(string DisplayName, string Email, bool IsAdmin, string[] Groups);
