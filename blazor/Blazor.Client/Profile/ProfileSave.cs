// Saving the profile through ImageThenDetailsSave: the avatar change (upload or removal) first, then the profile PUT,
// whose response asks the gateway to refresh the session's claims so the header shows the new name and avatar. After a
// failed step the current user is read back; the avatar is compared with the one the form was loaded with and the
// profile fields with the form as the API stores it.

using Account.Client;
using Blazor.Client.Components.Images;

namespace Blazor.Client.Profile;

public sealed record ProfileSaveOutcome(ImageThenDetailsSaveStatus Status, ApiCallResult? Failure, CurrentUserResponse? ConfirmedUser, bool AvatarSaved, bool ProfileSaved);

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
        var outcome = await ImageThenDetailsSave.RunAsync(
            avatarIntent,
            savedAvatarUrl,
            new ImageThenDetailsSaveCalls<CurrentUserResponse>(calls.UploadAvatar, calls.RemoveAvatar, calls.UpdateProfile, calls.ReadCurrentUser),
            user => user.AvatarUrl,
            user => IsProfileConfirmed(profile, user),
            abandoned
        );
        return new ProfileSaveOutcome(outcome.Status, outcome.Failure, outcome.Confirmed, outcome.ImageSaved, outcome.DetailsSaved);
    }

    public static bool IsProfileConfirmed(ProfileForm profile, CurrentUserResponse user)
    {
        return profile.FirstName == (user.FirstName ?? "") && profile.LastName == (user.LastName ?? "") && (profile.Title ?? "") == (user.Title ?? "");
    }
}
