namespace Blazor.Client.Users;

// The localized name and description of each user role, as the React edition shows them
public static class UserRoleText
{
    public static string Label(UserRole role)
    {
        return role switch
        {
            UserRole.Owner => UsersStrings.Owner,
            UserRole.Admin => UsersStrings.Admin,
            _ => UsersStrings.Member
        };
    }

    public static string Description(UserRole role)
    {
        return role switch
        {
            UserRole.Owner => UsersStrings.OwnerDescription,
            UserRole.Admin => UsersStrings.AdminDescription,
            _ => UsersStrings.MemberDescription
        };
    }
}
