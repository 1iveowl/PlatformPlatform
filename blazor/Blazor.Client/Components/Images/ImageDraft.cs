// The unsaved image change of a form that edits one picture, the profile's avatar or the account's logo: keep the saved
// image, upload a selected file (shown through its blob: preview) or remove the saved image. Selecting a file replaces an
// earlier selection; removing a selection that was never saved returns to the saved image. The file itself stays in the
// picker's module, which is asked for it when the save uploads it.

namespace Blazor.Client.Components.Images;

public enum ImageIntent
{
    Keep,
    Upload,
    Remove
}

public sealed class ImageDraft
{
    public ImageIntent Intent { get; private set; } = ImageIntent.Keep;

    // The declared type of the selected file, sent as the multipart part's content type
    public string? ContentType { get; private set; }

    public string? PreviewUrl { get; private set; }

    public bool HasChanges => Intent != ImageIntent.Keep;

    // The image the picker shows: the preview of a selection, nothing after a removal, otherwise the saved image
    public string? DisplayUrl(string? savedImageUrl)
    {
        return Intent switch
        {
            ImageIntent.Upload => PreviewUrl,
            ImageIntent.Remove => null,
            _ => savedImageUrl
        };
    }

    public void Select(string contentType, string previewUrl)
    {
        Intent = ImageIntent.Upload;
        ContentType = contentType;
        PreviewUrl = previewUrl;
    }

    public void Remove(string? savedImageUrl)
    {
        Intent = savedImageUrl is null ? ImageIntent.Keep : ImageIntent.Remove;
        ContentType = null;
        PreviewUrl = null;
    }

    public void Reset()
    {
        Intent = ImageIntent.Keep;
        ContentType = null;
        PreviewUrl = null;
    }
}
