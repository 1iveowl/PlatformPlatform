// The avatar picker's client-side check, the same limits the update-avatar endpoint's validator enforces (the contract's
// UpdateAvatarCommand), so an oversized or wrong-type file is refused before it is sent. The server stays the authority and
// also inspects the content; its messages reach the form alert as returned.

using System.Net;
using Account.Client;
using Account.Features.Users.Requests;
using Blazor.Client.Forms;

namespace Blazor.Client.Profile;

public static class AvatarFileRules
{
    public static string Accept { get; } = string.Join(",", UpdateAvatarCommand.AllowedContentTypes);

    // The message for a file the endpoint would reject, or null for an acceptable file. The type is checked first, as the
    // React edition's picker does.
    public static string? Validate(long size, string? contentType)
    {
        if (contentType is null || !UpdateAvatarCommand.AllowedContentTypes.Contains(contentType, StringComparer.Ordinal))
        {
            return AccountStrings.AvatarTypeInvalid;
        }

        return size > UpdateAvatarCommand.MaximumFileSizeInBytes ? AccountStrings.AvatarTooLarge : null;
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
