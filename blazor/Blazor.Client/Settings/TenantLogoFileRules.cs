// The logo picker's client-side limits, the same the update-logo endpoint's validator enforces (the contract's
// UpdateTenantLogoCommand), so an oversized or wrong-type file is refused before it is sent. The 2 MB limit is the
// endpoint's, where the React edition's picker stops at 1 MB.

using Account.Features.Tenants.Requests;
using Blazor.Client.Components.Images;

namespace Blazor.Client.Settings;

public static class TenantLogoFileRules
{
    public static ImageFileRules Rules { get; } = new(
        UpdateTenantLogoCommand.MaximumFileSizeInBytes,
        UpdateTenantLogoCommand.AllowedContentTypes,
        () => AccountStrings.LogoTooLarge,
        () => CommonStrings.ImageTypeNotSupported
    );
}
