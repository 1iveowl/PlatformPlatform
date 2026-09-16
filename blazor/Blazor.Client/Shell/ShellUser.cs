using Account.Features.Authentication.Queries;

namespace Blazor.Client.Shell;

// How the shell names the signed-in user from the bootstrap: the full name, else the email, and the avatar initials from
// the same fields, as the users list names a user.
public static class ShellUser
{
    public static string GetDisplayName(BootstrapUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return name.Length > 0 ? name : user.Email ?? "";
    }

    public static string GetInitials(BootstrapUser user)
    {
        var initials = string.Concat(new[] { user.FirstName, user.LastName }.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => char.ToUpperInvariant(name!.Trim()[0])));
        if (initials.Length > 0) return initials;

        return string.IsNullOrEmpty(user.Email) ? "" : user.Email[..1].ToUpperInvariant();
    }
}
