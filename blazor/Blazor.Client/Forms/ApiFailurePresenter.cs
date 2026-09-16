// The interactive presentation of a failed account API call, over the in-house ToastService. A surface that uses it renders
// a ToastRegion inside its interactive component. Field errors go to the form's FormErrorMapper when one is given; a message
// is an error toast with the detail, then the title; an antiforgery rejection is a toast whose action reloads the page as a
// new document; a 401 and a cancellation show nothing, because the unauthorized handler already navigates once.
// API text is shown unchanged, in English in every UI culture, and rendered as text.

using Account.Client;
using Microsoft.AspNetCore.Components;

namespace Blazor.Client.Forms;

public sealed class ApiFailurePresenter(ToastService toastService, NavigationManager navigationManager)
{
    public const string ErrorToastTestId = "api-failure-toast";
    public const string AntiforgeryToastTestId = "antiforgery-recovery-toast";

    public ApiFailure Present(ApiCallResult result, FormErrorMapper? formErrors = null)
    {
        return Present(result.Outcome, result.Problem ?? throw new ArgumentException("A successful result has no failure to present.", nameof(result)), formErrors);
    }

    public ApiFailure Present<TValue>(ApiCallResult<TValue> result, FormErrorMapper? formErrors = null)
    {
        return Present(result.Outcome, result.Problem ?? throw new ArgumentException("A successful result has no failure to present.", nameof(result)), formErrors);
    }

    public ApiFailure Present(ApiCallOutcome outcome, ApiCallProblem problem, FormErrorMapper? formErrors = null)
    {
        var failure = ApiFailureClassifier.Classify(outcome, problem);
        switch (failure.Kind)
        {
            case ApiFailureKind.FieldValidation when formErrors is not null:
                formErrors.Apply(problem);
                break;
            case ApiFailureKind.FieldValidation:
                // No form to place the field errors on: every message is kept, in one toast
                toastService.Show(ToastKind.Error, CommonStrings.SomethingWentWrong, string.Join(" ", problem.Errors.Values.SelectMany(messages => messages)), ErrorToastTestId);
                break;
            case ApiFailureKind.Message:
                toastService.Show(ToastKind.Error, CommonStrings.SomethingWentWrong, failure.Message, ErrorToastTestId);
                break;
            case ApiFailureKind.AntiforgeryRecovery:
                toastService.Show(ToastKind.Warning, failure.Message!, null, AntiforgeryToastTestId, CommonStrings.ReloadPage, ReloadPage);
                break;
            case ApiFailureKind.Suppressed:
                break;
        }

        return failure;
    }

    // A new document gets a new antiforgery token
    private void ReloadPage()
    {
        navigationManager.NavigateTo(navigationManager.Uri, true);
    }
}
