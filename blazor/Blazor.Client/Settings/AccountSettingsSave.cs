// Saving the account settings in the React edition's order: the logo change (upload or removal) first, then the account
// name PUT, whose response asks the gateway to refresh the session's claims so the shell shows the new name. The two are
// separate mutations, so a failure after the first one committed, or a lost response, never counts as a save and is never
// answered by sending the first mutation again blindly: the current tenant is read back, and the steps the server
// confirms are reported as saved so the form stops treating them as unsaved while the rest stays editable for a retry.
// Success is reported only when the PUT itself succeeded.

using Account.Client;
using Account.Features.Tenants.Queries;
using Blazor.Client.Components.Images;

namespace Blazor.Client.Settings;

public enum AccountSettingsSaveStatus
{
    // Every step succeeded
    Saved,

    // A step failed; Failure is the result to present, and LogoSaved and NameSaved say what the server confirmed
    Failed,

    // The authenticated surface is being left or the form was abandoned; nothing is presented
    Abandoned
}

public sealed record AccountSettingsSaveOutcome(AccountSettingsSaveStatus Status, ApiCallResult? Failure, TenantResponse? ConfirmedTenant, bool LogoSaved, bool NameSaved);

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
        try
        {
            if (logoIntent != ImageIntent.Keep)
            {
                var logoResult = await (logoIntent == ImageIntent.Upload ? calls.UploadLogo(abandoned) : calls.RemoveLogo(abandoned));
                if (logoResult is null || abandoned.IsCancellationRequested) return Abandoned();
                if (!logoResult.IsSuccess) return await ReconcileAsync(logoResult, false, logoIntent, savedLogoUrl, settings, calls, abandoned);
            }

            var nameResult = await calls.UpdateName(abandoned);
            if (nameResult is null || abandoned.IsCancellationRequested) return Abandoned();
            if (!nameResult.IsSuccess) return await ReconcileAsync(nameResult, true, logoIntent, savedLogoUrl, settings, calls, abandoned);

            return new AccountSettingsSaveOutcome(AccountSettingsSaveStatus.Saved, null, null, true, true);
        }
        catch (OperationCanceledException) when (abandoned.IsCancellationRequested)
        {
            return Abandoned();
        }
    }

    // An upload is confirmed by a stored logo other than the one the form was loaded with, a removal by no logo
    public static bool IsLogoConfirmed(ImageIntent logoIntent, string? savedLogoUrl, string? serverLogoUrl)
    {
        return logoIntent switch
        {
            ImageIntent.Upload => serverLogoUrl is not null && serverLogoUrl != savedLogoUrl,
            ImageIntent.Remove => serverLogoUrl is null,
            _ => true
        };
    }

    public static bool IsNameConfirmed(AccountSettingsForm settings, TenantResponse tenant)
    {
        return settings.Name == tenant.Name;
    }

    // What the server holds after a failed step. A 401 is left to the unauthorized handler, which is already leaving.
    private static async Task<AccountSettingsSaveOutcome> ReconcileAsync(
        ApiCallResult failure,
        bool logoStepSucceeded,
        ImageIntent logoIntent,
        string? savedLogoUrl,
        AccountSettingsForm settings,
        AccountSettingsSaveCalls calls,
        CancellationToken abandoned
    )
    {
        var logoSaved = logoIntent == ImageIntent.Keep || logoStepSucceeded;
        if (failure.Outcome == ApiCallOutcome.Unauthorized) return Abandoned();

        var current = await calls.ReadCurrentTenant(abandoned);
        if (current is null || abandoned.IsCancellationRequested) return Abandoned();
        if (!current.IsSuccess) return new AccountSettingsSaveOutcome(AccountSettingsSaveStatus.Failed, failure, null, logoSaved, false);

        var tenant = current.Value!;
        logoSaved = logoSaved || IsLogoConfirmed(logoIntent, savedLogoUrl, tenant.LogoUrl);
        return new AccountSettingsSaveOutcome(AccountSettingsSaveStatus.Failed, failure, tenant, logoSaved, IsNameConfirmed(settings, tenant));
    }

    private static AccountSettingsSaveOutcome Abandoned()
    {
        return new AccountSettingsSaveOutcome(AccountSettingsSaveStatus.Abandoned, null, null, false, false);
    }
}
