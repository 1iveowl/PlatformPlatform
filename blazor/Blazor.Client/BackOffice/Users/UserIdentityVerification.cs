// The decisions of the user detail's Identity tab, the React back office's UserIdentityVerificationSection and
// RevokeIdentityVerificationDialog: which state the tab renders from the account API's answer, who is offered the revoke,
// what a revoke's answer means and the texts the tab shows. The tab shows what the React section shows (the provider, the
// assurance level, when the verification was bound and when the person authenticated) and nothing else: the response carries
// no provider user id, and nothing here reads or keeps one.

using System.Globalization;
using Account.Client;
using Account.Features.BackOffice.Queries;
using Account.Features.ExternalAuthentication.BackOffice.Queries;
using Account.Features.ExternalAuthentication.Domain;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Shell;
using Blazor.Client.Profile;

namespace Blazor.Client.BackOffice.Users;

public enum UserIdentityVerificationView
{
    // The verification is being read
    Loading,

    // The read failed. As in the React back office this is never shown as not verified, which would be an untrue
    // statement about a person's identity rather than a missing value.
    Unavailable,

    // The account API reports no verification, the common case, which must not read as a failure
    Unverified,

    // The account API reports a verification
    Verified
}

public enum UserIdentityVerificationRevokeOutcome
{
    // The account API removed the verification
    Revoked,

    // The account API refused the identity (403): it is not in the admins group
    Refused,

    // The account API found no verification to revoke (404): another administrator revoked it first
    AlreadyRevoked,

    // Any other failure, a network failure included
    Failed,

    // A 401: the unauthorized handler is already leaving for the platform's login, so nothing is shown
    Leaving
}

public static class UserIdentityVerification
{
    public const string RevokedToastTestId = "identity-verification-revoked-toast";

    public static UserIdentityVerificationView GetView(ApiCallResult<BackOfficeUserIdentityVerificationResponse>? result)
    {
        if (result is null) return UserIdentityVerificationView.Loading;
        if (!result.IsSuccess || result.Value is null) return UserIdentityVerificationView.Unavailable;

        return result.Value.IsVerified ? UserIdentityVerificationView.Verified : UserIdentityVerificationView.Unverified;
    }

    // Offered only to an identity the account API reports in the admins group, and only for a verification the tab shows. The
    // account API refuses the revoke for anyone else; hiding is never the only guard.
    public static bool CanRevoke(MeResponse? me, UserIdentityVerificationView view)
    {
        return view == UserIdentityVerificationView.Verified && BackOfficeUser.CanRunAdminActions(me);
    }

    // Whatever the answer, the tab reads the verification again afterwards, so it shows what the account API stored and
    // never a state the page assumed
    public static UserIdentityVerificationRevokeOutcome FromRevoke(ApiCallResult result)
    {
        if (result.IsSuccess) return UserIdentityVerificationRevokeOutcome.Revoked;
        if (result.Outcome == ApiCallOutcome.Unauthorized) return UserIdentityVerificationRevokeOutcome.Leaving;

        return result.Problem?.StatusCode switch
        {
            403 => UserIdentityVerificationRevokeOutcome.Refused,
            404 => UserIdentityVerificationRevokeOutcome.AlreadyRevoked,
            _ => UserIdentityVerificationRevokeOutcome.Failed
        };
    }

    public static string GetProviderLabel(ExternalProviderType? provider)
    {
        return provider switch
        {
            ExternalProviderType.MitId => BackOfficeStrings.VerifiedWithMitId,
            ExternalProviderType.Google => BackOfficeStrings.VerifiedWithGoogle,
            ExternalProviderType.Entra => BackOfficeStrings.VerifiedWithMicrosoft,
            _ => BackOfficeStrings.VerifiedNoProvider
        };
    }

    public static string GetAssuranceLevelLabel(IdentityAssuranceLevel? assuranceLevel)
    {
        return IdentityVerificationState.GetAssuranceLevelLabel(assuranceLevel);
    }

    // The date in the culture's short date format, in the browser's time zone, and Unknown without one, as React shows it
    public static string FormatDate(DateTimeOffset? date)
    {
        return date is null ? AccountStrings.Unknown : AccountFormat.FormatDate(date);
    }

    // The confirmation names the user and says the user must verify again
    public static string GetRevokeConfirmation(string userName)
    {
        return string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.RevokeIdentityVerificationConfirmation, userName);
    }
}
