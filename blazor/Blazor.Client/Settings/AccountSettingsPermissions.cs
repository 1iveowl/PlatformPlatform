// Who may change the account's name and logo on the settings page, decided once for the name field, the logo picker, the
// Save button and the danger zone. The account API is the authority: UpdateCurrentTenant, UpdateTenantLogo and
// RemoveTenantLogo each refuse a caller who is not the owner, and this only decides which controls render.

using Account.Features.Authentication.Queries;

namespace Blazor.Client.Settings;

public static class AccountSettingsPermissions
{
    // Every authenticated user may open the page; only the owner edits it
    public static bool CanEditAccount(BootstrapUser? currentUser)
    {
        return currentUser?.Role == nameof(UserRole.Owner);
    }

    // The explanation under the read-only name field, or null for the owner, who needs none
    public static string? ReadOnlyDescription(BootstrapUser? currentUser)
    {
        return CanEditAccount(currentUser) ? null : AccountStrings.OnlyOwnersCanModifyAccountName;
    }
}
