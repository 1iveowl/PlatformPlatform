namespace Blazor.Client.Users;

// Client-side presentation of the shared UserDetails contract: the name shown in the list, the side pane and the role
// dialog, and the initials of an avatar without an image. An invited user who has not logged in yet has no first name,
// last name or title; the API returns null for them.
public static class UserDetailsExtensions
{
    extension(UserDetails user)
    {
        public string DisplayName => $"{user.FirstName} {user.LastName}".Trim();

        public string DisplayNameOrEmail => user.DisplayName.Length > 0 ? user.DisplayName : user.Email;

        public string Initials => user.DisplayName.Length > 0
            ? string.Concat(new[] { user.FirstName, user.LastName }.Where(name => !string.IsNullOrEmpty(name)).Select(name => char.ToUpperInvariant(name![0])))
            : user.Email[..Math.Min(1, user.Email.Length)].ToUpperInvariant();
    }
}
