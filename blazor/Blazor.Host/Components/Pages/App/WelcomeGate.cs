using System.Security.Claims;
using Account.Features.Users.Domain;
using Blazor.Client;

namespace Blazor.Host.Components.Pages.App;

public enum WelcomeStep
{
    Account,
    Profile,
    Done
}

// Decides from the signed-in user's claims whether the welcome setup is still required: the account name for an owner
// whose tenant has no name, then the profile for a user without a first name. The claims come from the access token the
// gateway derives from the session cookies; the account API signals a token refresh after a tenant or profile update, so
// the document request that follows a welcome step already carries the updated claims.
public static class WelcomeGate
{
    public static readonly string WelcomePath = AppUrls.ToAbsolute("welcome");

    public static WelcomeStep GetStep(ClaimsPrincipal user)
    {
        var isOwner = user.FindFirstValue(ClaimTypes.Role) == nameof(UserRole.Owner);
        if (isOwner && string.IsNullOrWhiteSpace(user.FindFirstValue("tenant_name"))) return WelcomeStep.Account;
        if (string.IsNullOrWhiteSpace(user.FindFirstValue(ClaimTypes.GivenName))) return WelcomeStep.Profile;
        return WelcomeStep.Done;
    }

    // The sanitized destination after welcome; welcome itself is never a destination, so it cannot redirect to itself
    public static string GetDestination(string? returnPath)
    {
        var destination = AppUrls.SanitizeReturnPath(returnPath);
        var isWelcome = destination == WelcomePath || destination.StartsWith($"{WelcomePath}?", StringComparison.Ordinal) ||
                        destination.StartsWith($"{WelcomePath}/", StringComparison.Ordinal) || destination.StartsWith($"{WelcomePath}#", StringComparison.Ordinal);
        return isWelcome ? AppUrls.AuthenticatedHome : destination;
    }

    public static string GetWelcomeUrl(string? returnPath)
    {
        return $"{WelcomePath}?returnPath={Uri.EscapeDataString(GetDestination(returnPath))}";
    }
}
