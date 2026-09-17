using Account.Client;
using Blazor.Client.Forms;
using SharedKernel.Localization;

namespace Blazor.Host.Components.Pages.Public;

public enum VerificationOutcomeKind
{
    // No completion or resend failed, or it failed in a way that is shown as its message alone
    None,

    // A 400 from completing while the code is still shown as valid
    WrongCode,

    // A 400 from completing once the page shows the code as expired
    Expired,

    // A 403 from completing (too many attempts) or from resending (too many resends)
    Locked
}

// The state a verification page shows for a failed completion or resend. The account API decides the outcome and its text
// is shown as returned beneath the state label; the status code picks the state. The API returns 400 for both a wrong and
// an expired code, so the page's displayed expiry (VerificationFlow) tells them apart, as the React verification page does
// with its countdown. The label is display only: the API stays authoritative for attempts, resends and expiry.
public sealed record VerificationOutcome(VerificationOutcomeKind Kind)
{
    public static readonly VerificationOutcome None = new(VerificationOutcomeKind.None);

    public bool IsLocked => Kind == VerificationOutcomeKind.Locked;

    public string? Label => Kind switch
    {
        VerificationOutcomeKind.WrongCode => AuthenticationStrings.VerificationStateWrongCode,
        VerificationOutcomeKind.Expired => AuthenticationStrings.VerificationStateExpired,
        VerificationOutcomeKind.Locked => AuthenticationStrings.VerificationStateLocked,
        _ => null
    };

    // The value of the data-verification-state attribute the browser harness and specifications read
    public string? StateName => Kind switch
    {
        VerificationOutcomeKind.WrongCode => "wrong-code",
        VerificationOutcomeKind.Expired => "expired",
        VerificationOutcomeKind.Locked => "locked",
        _ => null
    };

    // Only a failure presented as a message has a state; field errors, an antiforgery rejection, a transport failure and a
    // suppressed failure keep their own presentation
    public static VerificationOutcome FromFailure(ApiFailure failure, ApiCallProblem? problem, bool isShownAsExpired)
    {
        if (failure.Kind != ApiFailureKind.Message) return None;

        return problem?.StatusCode switch
        {
            400 => new VerificationOutcome(isShownAsExpired ? VerificationOutcomeKind.Expired : VerificationOutcomeKind.WrongCode),
            403 => new VerificationOutcome(VerificationOutcomeKind.Locked),
            _ => None
        };
    }
}
