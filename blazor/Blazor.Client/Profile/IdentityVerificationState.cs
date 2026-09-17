using System.Globalization;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Queries;

namespace Blazor.Client.Profile;

// What the identity verification section on the profile shows
public enum IdentityVerificationView
{
    // The mitid-verification flag is off, or the flags are not known yet (prerender)
    Hidden,

    // The verification status is being read
    Loading,

    // The status could not be read; as in the React edition, the section keeps its explanation and offers no button
    Unavailable,

    // Not verified with MitID: the explanation and the MitID button
    Unverified,

    // The start was sent and the document is about to leave for the identity provider; the button stays disabled
    Redirecting,

    // Verified with MitID: the wordmark, the assurance level and the date
    Verified
}

// The decisions of the identity verification section, the React edition's MitIdVerificationSection: which state it renders,
// when a start may be sent, where the verification returns to and which URL the document may leave for. The component
// reports what happened and renders View; it decides nothing itself.
public sealed class IdentityVerificationState
{
    public const ExternalProviderType Provider = ExternalProviderType.MitId;

    private bool _isLoaded;
    private bool _isRedirecting;
    private bool _loadFailed;

    // The profile page under the path base, which the account API validates again as a return path inside this edition
    public static string ReturnPath => AppUrls.ToAbsolute("user/profile");

    // The status only when it is a MitID verification: the status covers every provider that can verify an identity, and
    // this section speaks for MitID alone
    public VerificationStatusResponse? MitIdVerification { get; private set; }

    public IdentityVerificationView GetView(bool isVerificationEnabled)
    {
        if (!isVerificationEnabled) return IdentityVerificationView.Hidden;
        if (!_isLoaded) return IdentityVerificationView.Loading;
        if (MitIdVerification is not null) return IdentityVerificationView.Verified;
        if (_loadFailed) return IdentityVerificationView.Unavailable;
        return _isRedirecting ? IdentityVerificationView.Redirecting : IdentityVerificationView.Unverified;
    }

    // The status is read once, when the flag is first known to be on
    public bool ShouldLoad(bool isVerificationEnabled)
    {
        return isVerificationEnabled && !_isLoaded;
    }

    public void Loaded(VerificationStatusResponse status)
    {
        _isLoaded = true;
        _loadFailed = false;
        MitIdVerification = status is { IsVerified: true, Provider: Provider } ? status : null;
    }

    public void LoadFailed()
    {
        _isLoaded = true;
        _loadFailed = true;
        MitIdVerification = null;
    }

    // Returns false when a start may not be sent: the section is not offering the button, or a start is already pending,
    // so a second click never sends a second mutation
    public bool TryBeginStart(bool isVerificationEnabled)
    {
        if (GetView(isVerificationEnabled) != IdentityVerificationView.Unverified) return false;

        _isRedirecting = true;
        return true;
    }

    // A failed or abandoned start clears the pending state, so the button is offered again for an explicit retry
    public void StartFailed()
    {
        _isRedirecting = false;
    }

    // The authorization URL is the account API's provider contract, so it is used as returned, but only when it is an
    // absolute http or https URL: anything else is not a document the browser may leave for
    public static bool IsNavigableAuthorizationUrl(string? authorizationUrl)
    {
        return Uri.TryCreate(authorizationUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    // The verified date in the culture's short date format, in the browser's time zone, as the users list shows dates
    public static string FormatVerifiedDate(DateTimeOffset? verifiedAt)
    {
        return verifiedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? "";
    }

    public static string GetAssuranceLevelLabel(IdentityAssuranceLevel? assuranceLevel)
    {
        return assuranceLevel switch
        {
            IdentityAssuranceLevel.Low => AccountStrings.LowAssurance,
            IdentityAssuranceLevel.Substantial => AccountStrings.SubstantialAssurance,
            IdentityAssuranceLevel.High => AccountStrings.HighAssurance,
            _ => AccountStrings.UnknownAssurance
        };
    }
}
