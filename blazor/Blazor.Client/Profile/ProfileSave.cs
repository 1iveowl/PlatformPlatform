// Saving the profile in the React edition's order: the avatar change (upload or removal) first, then the profile PUT, whose
// response asks the gateway to refresh the session's claims so the header shows the new name and avatar. The two are
// separate mutations, so a failure after the first one committed, or a lost response, never counts as a save and is never
// answered by sending the first mutation again blindly: the current user is read back, and the steps the server confirms
// are reported as saved so the form stops treating them as unsaved while the rest stays editable for a retry. Success is
// reported only when the PUT itself succeeded.

using Account.Client;
using Blazor.Client.Components.Images;

namespace Blazor.Client.Profile;

public enum ProfileSaveStatus
{
    // Every step succeeded
    Saved,

    // A step failed; Failure is the result to present, and AvatarSaved and ProfileSaved say what the server confirmed
    Failed,

    // The authenticated surface is being left or the form was abandoned; nothing is presented
    Abandoned
}

public sealed record ProfileSaveOutcome(ProfileSaveStatus Status, ApiCallResult? Failure, CurrentUserResponse? ConfirmedUser, bool AvatarSaved, bool ProfileSaved);

// Each call returns null when the authenticated surface is being left (SessionState.UnlessLeavingAsync)
public sealed record ProfileSaveCalls(
    Func<CancellationToken, Task<ApiCallResult?>> UploadAvatar,
    Func<CancellationToken, Task<ApiCallResult?>> RemoveAvatar,
    Func<CancellationToken, Task<ApiCallResult?>> UpdateProfile,
    Func<CancellationToken, Task<ApiCallResult<CurrentUserResponse>?>> ReadCurrentUser
);

public static class ProfileSave
{
    // profile is the form as the API stores it (trimmed); savedAvatarUrl is the avatar the form was loaded with. Cancelling
    // abandoned (the form was disposed) stops the save and discards any result that arrives afterwards.
    public static async Task<ProfileSaveOutcome> RunAsync(ImageIntent avatarIntent, string? savedAvatarUrl, ProfileForm profile, ProfileSaveCalls calls, CancellationToken abandoned)
    {
        try
        {
            if (avatarIntent != ImageIntent.Keep)
            {
                var avatarResult = await (avatarIntent == ImageIntent.Upload ? calls.UploadAvatar(abandoned) : calls.RemoveAvatar(abandoned));
                if (avatarResult is null || abandoned.IsCancellationRequested) return Abandoned();
                if (!avatarResult.IsSuccess) return await ReconcileAsync(avatarResult, false, avatarIntent, savedAvatarUrl, profile, calls, abandoned);
            }

            var profileResult = await calls.UpdateProfile(abandoned);
            if (profileResult is null || abandoned.IsCancellationRequested) return Abandoned();
            if (!profileResult.IsSuccess) return await ReconcileAsync(profileResult, true, avatarIntent, savedAvatarUrl, profile, calls, abandoned);

            return new ProfileSaveOutcome(ProfileSaveStatus.Saved, null, null, true, true);
        }
        catch (OperationCanceledException) when (abandoned.IsCancellationRequested)
        {
            return Abandoned();
        }
    }

    // What the server holds after a failed step. A 401 is left to the unauthorized handler, which is already leaving.
    private static async Task<ProfileSaveOutcome> ReconcileAsync(
        ApiCallResult failure,
        bool avatarStepSucceeded,
        ImageIntent avatarIntent,
        string? savedAvatarUrl,
        ProfileForm profile,
        ProfileSaveCalls calls,
        CancellationToken abandoned
    )
    {
        var avatarSaved = avatarIntent == ImageIntent.Keep || avatarStepSucceeded;
        if (failure.Outcome == ApiCallOutcome.Unauthorized) return Abandoned();

        var current = await calls.ReadCurrentUser(abandoned);
        if (current is null || abandoned.IsCancellationRequested) return Abandoned();
        if (!current.IsSuccess) return new ProfileSaveOutcome(ProfileSaveStatus.Failed, failure, null, avatarSaved, false);

        var user = current.Value!;
        avatarSaved = avatarSaved || IsAvatarConfirmed(avatarIntent, savedAvatarUrl, user.AvatarUrl);
        return new ProfileSaveOutcome(ProfileSaveStatus.Failed, failure, user, avatarSaved, IsProfileConfirmed(profile, user));
    }

    // An upload is confirmed by a stored avatar other than the one the form was loaded with, a removal by no avatar
    public static bool IsAvatarConfirmed(ImageIntent avatarIntent, string? savedAvatarUrl, string? serverAvatarUrl)
    {
        return avatarIntent switch
        {
            ImageIntent.Upload => serverAvatarUrl is not null && serverAvatarUrl != savedAvatarUrl,
            ImageIntent.Remove => serverAvatarUrl is null,
            _ => true
        };
    }

    public static bool IsProfileConfirmed(ProfileForm profile, CurrentUserResponse user)
    {
        return profile.FirstName == (user.FirstName ?? "") && profile.LastName == (user.LastName ?? "") && (profile.Title ?? "") == (user.Title ?? "");
    }

    private static ProfileSaveOutcome Abandoned()
    {
        return new ProfileSaveOutcome(ProfileSaveStatus.Abandoned, null, null, false, false);
    }
}
