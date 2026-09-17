// What the signed-in user may do to other users on the users page, decided once for the toolbar, the row menu and the side
// pane: change a role, invite, and delete one or many. Each decision mirrors the order its command checks on the server
// (ChangeUserRole and DeleteUser: yourself first, then the owner requirement; BulkDeleteUsers: the owner requirement,
// then yourself). The server stays the authority; this only decides whether a control shows, whether it is enabled and
// which reason explains a disabled one.

using Account.Features.Authentication.Queries;
using SharedKernel.Domain;

namespace Blazor.Client.Users;

public enum RoleChangePermission
{
    Allowed,
    OwnRole,
    NotOwner
}

public enum UserDeletePermission
{
    Allowed,
    Self,
    NotOwner
}

public static class UserRolePermissions
{
    // Only owners see the role action in the row menu; it is then disabled on their own row
    public static bool IsOwner(BootstrapUser? currentUser)
    {
        return currentUser?.Role == nameof(UserRole.Owner);
    }

    public static RoleChangePermission ForTarget(BootstrapUser? currentUser, UserId targetUserId)
    {
        if (currentUser is not null && currentUser.Id == targetUserId) return RoleChangePermission.OwnRole;
        return IsOwner(currentUser) ? RoleChangePermission.Allowed : RoleChangePermission.NotOwner;
    }

    public static string? DisabledReason(RoleChangePermission permission)
    {
        return permission switch
        {
            RoleChangePermission.OwnRole => UsersStrings.CannotChangeOwnRole,
            RoleChangePermission.NotOwner => UsersStrings.OnlyOwnersCanChangeRoles,
            _ => null
        };
    }

    // The toolbar's invite action: an owner with no more than one row selected, as the React toolbar shows it
    public static bool CanInvite(BootstrapUser? currentUser, int selectedCount)
    {
        return IsOwner(currentUser) && selectedCount < 2;
    }

    // The row menu's delete action; like the role action, only an owner sees it and it is disabled on their own row
    public static UserDeletePermission ForDelete(BootstrapUser? currentUser, UserId targetUserId)
    {
        if (currentUser is not null && currentUser.Id == targetUserId) return UserDeletePermission.Self;
        return IsOwner(currentUser) ? UserDeletePermission.Allowed : UserDeletePermission.NotOwner;
    }

    // The toolbar's bulk delete action shows for an owner with two or more rows selected
    public static bool ShowsBulkDelete(BootstrapUser? currentUser, int selectedCount)
    {
        return IsOwner(currentUser) && selectedCount >= 2;
    }

    // Disabled while the selection includes the signed-in user
    public static UserDeletePermission ForBulkDelete(BootstrapUser? currentUser, IReadOnlyCollection<UserId> targetUserIds)
    {
        if (!IsOwner(currentUser)) return UserDeletePermission.NotOwner;
        return targetUserIds.Contains(currentUser!.Id) ? UserDeletePermission.Self : UserDeletePermission.Allowed;
    }

    // Only the own-account case is explained; a non-owner never sees a delete action
    public static string? DeleteDisabledReason(UserDeletePermission permission)
    {
        return permission == UserDeletePermission.Self ? UsersStrings.CannotDeleteYourself : null;
    }
}
