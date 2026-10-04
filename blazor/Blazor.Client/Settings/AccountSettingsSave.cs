// Saving the account settings through ImageThenDetailsSave: the logo change (upload or removal) first, then the account
// name PUT, whose response asks the gateway to refresh the session's claims so the shell shows the new name. After a
// failed step the current tenant is read back; the logo is compared with the one the form was loaded with and the name
// with the form as the API stores it.

using Account.Client;
using Account.Features.Tenants.Queries;
using Blazor.Client.Components.Images;

namespace Blazor.Client.Settings;

public sealed record AccountSettingsSaveOutcome(ImageThenDetailsSaveStatus Status, ApiCallResult? Failure, TenantResponse? ConfirmedTenant, bool LogoSaved, bool NameSaved);

// Each call returns null when the authenticated surface is being left (SessionState.UnlessLeavingAsync)
public sealed record AccountSettingsSaveCalls(
    Func<CancellationToken, Task<ApiCallResult?>> UploadLogo,
    Func<CancellationToken, Task<ApiCallResult?>> RemoveLogo,
    Func<CancellationToken, Task<ApiCallResult?>> UpdateName,
    Func<CancellationToken, Task<ApiCallResult<TenantResponse>?>> ReadCurrentTenant
);

public static class AccountSettingsSave
{
    // settings is the form as the API stores it; savedLogoUrl is the logo the form was loaded with. Cancelling abandoned
    // (the form was disposed, or the account was left) stops the save and discards any result that arrives afterwards.
    public static async Task<AccountSettingsSaveOutcome> RunAsync(
        ImageIntent logoIntent,
        string? savedLogoUrl,
        AccountSettingsForm settings,
        AccountSettingsSaveCalls calls,
        CancellationToken abandoned
    )
    {
        var outcome = await ImageThenDetailsSave.RunAsync(
            logoIntent,
            savedLogoUrl,
            new ImageThenDetailsSaveCalls<TenantResponse>(calls.UploadLogo, calls.RemoveLogo, calls.UpdateName, calls.ReadCurrentTenant),
            tenant => tenant.LogoUrl,
            tenant => IsNameConfirmed(settings, tenant),
            abandoned
        );
        return new AccountSettingsSaveOutcome(outcome.Status, outcome.Failure, outcome.Confirmed, outcome.ImageSaved, outcome.DetailsSaved);
    }

    public static bool IsNameConfirmed(AccountSettingsForm settings, TenantResponse tenant)
    {
        return settings.Name == tenant.Name;
    }
}
