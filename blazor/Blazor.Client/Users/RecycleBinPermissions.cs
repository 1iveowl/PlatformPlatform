// What the signed-in user may do in the users recycle bin, decided once for the tabs, the page, its toolbar and the permanent
// delete dialog. The rules mirror the account API: owners and admins view, restore and permanently delete one user; only
// owners permanently delete several users at once or empty the recycle bin. The server stays the authority and its
// refusals are shown as returned; this only decides which controls show.

using Account.Features.Authentication.Queries;

namespace Blazor.Client.Users;

public sealed record RecycleBinActions(bool ShowsEmptyRecycleBin, bool ShowsRestore, bool ShowsPermanentDelete)
{
    public static readonly RecycleBinActions None = new(false, false, false);
}

public static class RecycleBinPermissions
{
    // The recycle bin tab and page: owners and admins
    public static bool CanView(BootstrapUser? currentUser)
    {
        return currentUser?.Role is nameof(UserRole.Owner) or nameof(UserRole.Admin);
    }

    // One user through purge (owners and admins), several through bulk purge (owners only)
    public static bool CanPermanentlyDelete(BootstrapUser? currentUser, int selectedCount)
    {
        return selectedCount switch
        {
            <= 0 => false,
            1 => CanView(currentUser),
            _ => UserRolePermissions.IsOwner(currentUser)
        };
    }

    public static bool CanEmpty(BootstrapUser? currentUser)
    {
        return UserRolePermissions.IsOwner(currentUser);
    }

    // With a selection the toolbar offers Restore and Delete; without one, Empty recycle bin while the bin has users
    public static RecycleBinActions Actions(BootstrapUser? currentUser, int selectedCount, int totalCount)
    {
        if (!CanView(currentUser)) return RecycleBinActions.None;
        if (selectedCount > 0) return new RecycleBinActions(false, true, CanPermanentlyDelete(currentUser, selectedCount));
        return new RecycleBinActions(totalCount > 0 && CanEmpty(currentUser), false, false);
    }
}
