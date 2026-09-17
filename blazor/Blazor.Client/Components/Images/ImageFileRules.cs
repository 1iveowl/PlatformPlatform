// The client-side check of one picked image, built from the limits of the endpoint that will receive it (the contract's
// UpdateAvatarCommand or UpdateTenantLogoCommand), so an oversized or wrong-type file is refused before it is sent. The
// limits are given to this class rather than copied into it, and the messages are read per call so a culture change is
// never frozen into a captured string. The server stays the authority and also inspects the content; its messages reach
// the form alert as returned.

using System.Net;
using Account.Client;
using Blazor.Client.Forms;

namespace Blazor.Client.Components.Images;

public sealed class ImageFileRules(long maximumFileSizeInBytes, IReadOnlyList<string> allowedContentTypes, Func<string> tooLargeMessage, Func<string> typeInvalidMessage)
{
    public long MaximumFileSizeInBytes { get; } = maximumFileSizeInBytes;

    // The accept attribute of the file input, the allowed types in the order the endpoint lists them
    public string Accept { get; } = string.Join(",", allowedContentTypes);

    // The message for a file the endpoint would reject, or null for an acceptable file. The type is checked first, as the
    // React edition's pickers do.
    public string? Validate(long size, string? contentType)
    {
        if (contentType is null || !allowedContentTypes.Contains(contentType, StringComparer.Ordinal))
        {
            return typeInvalidMessage();
        }

        return size > MaximumFileSizeInBytes ? tooLargeMessage() : null;
    }

    // The endpoint answers a file past its form limit with a 400 without a body, and a request past its size limit with a
    // 413 without a body, so neither carries a message to show as returned; both mean the image is too large
    public static bool IsTooLargeResponse(ApiCallResult result)
    {
        if (result.IsSuccess || result.Outcome != ApiCallOutcome.Failure || result.Problem is not { } problem) return false;

        return problem.StatusCode == (int)HttpStatusCode.RequestEntityTooLarge
               || (problem is { StatusCode: (int)HttpStatusCode.BadRequest, Detail: null, Errors.Count: 0 } && !ApiFailureClassifier.IsAntiforgeryFailure(problem));
    }
}
