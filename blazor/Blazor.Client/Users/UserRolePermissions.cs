// Whether the signed-in user may change another user's role, decided once for the row menu and the side pane. It mirrors
// the order ChangeUserRole checks on the server: your own role first, then the owner requirement. The server stays the
// authority; this only decides whether the control is enabled and which reason explains a disabled one.

using Account.Features.Authentication.Queries;
using SharedKernel.Domain;

namespace Blazor.Client.Users;

public enum RoleChangePermission
{
    Allowed,
    OwnRole,
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
}
