// Every way out of the authenticated WebAssembly surface is a full document navigation, so no identity, tenant or
// cached API response survives in memory: to the login page with the return path, or to the error page when the
// account API names why the session ended. Mirrors the React edition's AuthenticationMiddleware.

using Microsoft.AspNetCore.Components;

namespace Blazor.Client.Bootstrap;

public sealed class AuthenticationNavigator(NavigationManager navigationManager)
{
    public const string UnauthorizedReasonHeaderName = "x-unauthorized-reason";

    private bool _isLeaving;

    // The values of SharedKernel's UnauthorizedReason that end a session for good; ReplayAttackDetected and an absent
    // reason send the user to sign in again
    public static string? GetErrorCode(string? unauthorizedReason)
    {
        return unauthorizedReason switch
        {
            "Revoked" => "session_revoked",
            "SessionNotFound" => "session_not_found",
            "TenantDeleted" => "tenant_deleted",
            _ => null
        };
    }

    public void LeaveForUnauthorized(string? unauthorizedReason)
    {
        var errorCode = GetErrorCode(unauthorizedReason);
        if (errorCode is null)
        {
            LeaveForLogin();
            return;
        }

        Leave($"{AppUrls.ToAbsolute("error")}?error={Uri.EscapeDataString(errorCode)}");
    }

    public void LeaveForLogin()
    {
        var returnPath = AppUrls.SanitizeReturnPath(new Uri(navigationManager.Uri).PathAndQuery);
        Leave($"{AppUrls.ToAbsolute("login")}?returnPath={Uri.EscapeDataString(returnPath)}");
    }

    public void LeaveForLoggedOut()
    {
        Leave(AppUrls.ToAbsolute("login"));
    }

    public void LeaveForAuthenticatedHome()
    {
        Leave(AppUrls.AuthenticatedHome);
    }

    // The first destination wins: a 401 that already chose the error page is not overridden by a later check that only
    // sees an anonymous bootstrap
    private void Leave(string url)
    {
        if (_isLeaving) return;
        _isLeaving = true;
        navigationManager.NavigateTo(url, true);
    }
}
