// Saving a form that edits one picture and some details, in the React edition's order: the image change (upload or
// removal) first, then the details PUT. The two are separate mutations, so a failure after the first one committed, or a
// lost response, never counts as a save and is never answered by sending a mutation again: the stored state is read back
// once, and the steps the server confirms are reported as saved so the form stops treating them as unsaved while the
// rest stays editable for a retry. Success is reported only when the PUT itself succeeded. The profile (ProfileSave) and
// the account settings (AccountSettingsSave) adapt this with their own calls and their own comparison of the details.

using Account.Client;

namespace Blazor.Client.Components.Images;

public enum ImageThenDetailsSaveStatus
{
    // Every step succeeded
    Saved,

    // A step failed; Failure is the result to present, and ImageSaved and DetailsSaved say what the server confirmed
    Failed,

    // The authenticated surface is being left or the form was abandoned; nothing is presented
    Abandoned
}

public sealed record ImageThenDetailsSaveOutcome<TStored>(ImageThenDetailsSaveStatus Status, ApiCallResult? Failure, TStored? Confirmed, bool ImageSaved, bool DetailsSaved)
    where TStored : class;

// Each call returns null when the authenticated surface is being left (SessionState.UnlessLeavingAsync)
public sealed record ImageThenDetailsSaveCalls<TStored>(
    Func<CancellationToken, Task<ApiCallResult?>> UploadImage,
    Func<CancellationToken, Task<ApiCallResult?>> RemoveImage,
    Func<CancellationToken, Task<ApiCallResult?>> SaveDetails,
    Func<CancellationToken, Task<ApiCallResult<TStored>?>> ReadStored
) where TStored : class;

public static class ImageThenDetailsSave
{
    // savedImageUrl is the image the form was loaded with; storedImageUrl and isDetailsConfirmed read the stored state the
    // read-back returns. Cancelling abandoned (the form was disposed) stops the save and discards any result that arrives
    // afterwards.
    public static async Task<ImageThenDetailsSaveOutcome<TStored>> RunAsync<TStored>(
        ImageIntent imageIntent,
        string? savedImageUrl,
        ImageThenDetailsSaveCalls<TStored> calls,
        Func<TStored, string?> storedImageUrl,
        Func<TStored, bool> isDetailsConfirmed,
        CancellationToken abandoned
    ) where TStored : class
    {
        try
        {
            if (imageIntent != ImageIntent.Keep)
            {
                var imageResult = await (imageIntent == ImageIntent.Upload ? calls.UploadImage(abandoned) : calls.RemoveImage(abandoned));
                if (imageResult is null || abandoned.IsCancellationRequested) return Abandoned<TStored>();
                if (!imageResult.IsSuccess) return await ReconcileAsync(imageResult, false, imageIntent, savedImageUrl, calls, storedImageUrl, isDetailsConfirmed, abandoned);
            }

            var detailsResult = await calls.SaveDetails(abandoned);
            if (detailsResult is null || abandoned.IsCancellationRequested) return Abandoned<TStored>();
            if (!detailsResult.IsSuccess) return await ReconcileAsync(detailsResult, true, imageIntent, savedImageUrl, calls, storedImageUrl, isDetailsConfirmed, abandoned);

            return new ImageThenDetailsSaveOutcome<TStored>(ImageThenDetailsSaveStatus.Saved, null, null, true, true);
        }
        catch (OperationCanceledException) when (abandoned.IsCancellationRequested)
        {
            return Abandoned<TStored>();
        }
    }

    // An upload is confirmed by a stored image other than the one the form was loaded with, a removal by no image
    public static bool IsImageConfirmed(ImageIntent imageIntent, string? savedImageUrl, string? storedImageUrl)
    {
        return imageIntent switch
        {
            ImageIntent.Upload => storedImageUrl is not null && storedImageUrl != savedImageUrl,
            ImageIntent.Remove => storedImageUrl is null,
            _ => true
        };
    }

    // What the server holds after a failed step. A 401 is left to the unauthorized handler, which is already leaving.
    private static async Task<ImageThenDetailsSaveOutcome<TStored>> ReconcileAsync<TStored>(
        ApiCallResult failure,
        bool imageStepSucceeded,
        ImageIntent imageIntent,
        string? savedImageUrl,
        ImageThenDetailsSaveCalls<TStored> calls,
        Func<TStored, string?> storedImageUrl,
        Func<TStored, bool> isDetailsConfirmed,
        CancellationToken abandoned
    ) where TStored : class
    {
        var imageSaved = imageIntent == ImageIntent.Keep || imageStepSucceeded;
        if (failure.Outcome == ApiCallOutcome.Unauthorized) return Abandoned<TStored>();

        var current = await calls.ReadStored(abandoned);
        if (current is null || abandoned.IsCancellationRequested) return Abandoned<TStored>();
        if (!current.IsSuccess) return new ImageThenDetailsSaveOutcome<TStored>(ImageThenDetailsSaveStatus.Failed, failure, null, imageSaved, false);

        var stored = current.Value;
        imageSaved = imageSaved || IsImageConfirmed(imageIntent, savedImageUrl, storedImageUrl(stored));
        return new ImageThenDetailsSaveOutcome<TStored>(ImageThenDetailsSaveStatus.Failed, failure, stored, imageSaved, isDetailsConfirmed(stored));
    }

    private static ImageThenDetailsSaveOutcome<TStored> Abandoned<TStored>() where TStored : class
    {
        return new ImageThenDetailsSaveOutcome<TStored>(ImageThenDetailsSaveStatus.Abandoned, null, null, false, false);
    }
}
