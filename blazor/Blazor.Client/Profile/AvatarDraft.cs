// The unsaved avatar change of the profile form: keep the saved avatar, upload a selected file (shown through its blob:
// preview) or remove the saved avatar. Selecting a file replaces an earlier selection; removing a selection that was never
// saved returns to the saved avatar. The file itself stays in the picker's module, which is asked for it when the save
// uploads it.

namespace Blazor.Client.Profile;

public enum AvatarIntent
{
    Keep,
    Upload,
    Remove
}

public sealed class AvatarDraft
{
    public AvatarIntent Intent { get; private set; } = AvatarIntent.Keep;

    // The declared type of the selected file, sent as the multipart part's content type
    public string? ContentType { get; private set; }

    public string? PreviewUrl { get; private set; }

    public bool HasChanges => Intent != AvatarIntent.Keep;

    // The image the picker shows: the preview of a selection, nothing after a removal, otherwise the saved avatar
    public string? DisplayUrl(string? savedAvatarUrl)
    {
        return Intent switch
        {
            AvatarIntent.Upload => PreviewUrl,
            AvatarIntent.Remove => null,
            _ => savedAvatarUrl
        };
    }

    public void Select(string contentType, string previewUrl)
    {
        Intent = AvatarIntent.Upload;
        ContentType = contentType;
        PreviewUrl = previewUrl;
    }

    public void Remove(string? savedAvatarUrl)
    {
        Intent = savedAvatarUrl is null ? AvatarIntent.Keep : AvatarIntent.Remove;
        ContentType = null;
        PreviewUrl = null;
    }

    public void Reset()
    {
        Intent = AvatarIntent.Keep;
        ContentType = null;
        PreviewUrl = null;
    }
}
