// The avatar picker's client-side limits, the same the update-avatar endpoint's validator enforces (the contract's
// UpdateAvatarCommand), so an oversized or wrong-type file is refused before it is sent.

using Account.Features.Users.Requests;
using Blazor.Client.Components.Images;

namespace Blazor.Client.Profile;

public static class AvatarFileRules
{
    public static ImageFileRules Rules { get; } = new(
        UpdateAvatarCommand.MaximumFileSizeInBytes,
        UpdateAvatarCommand.AllowedContentTypes,
        () => AccountStrings.AvatarTooLarge,
        () => CommonStrings.ImageTypeNotSupported
    );
}
