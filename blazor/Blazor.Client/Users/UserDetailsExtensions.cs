namespace Blazor.Client.Users;

// Client-side presentation of the shared UserDetails contract; the name shown in lists and the side pane.
public static class UserDetailsExtensions
{
    extension(UserDetails user)
    {
        public string DisplayName => $"{user.FirstName} {user.LastName}".Trim();
    }
}
