// How a failed account API call is presented, decided once for static server-rendered forms and interactive surfaces.
// Mirrors the React edition's errorHandler.ts: a message uses the detail, then the title; no generic message for a 401,
// which the authentication handling already acts on, or for a 400 whose title names antiforgery, which gets a recovery
// action instead. API text is passed through unchanged, in English in every UI culture.

using System.Globalization;
using Account.Client;

namespace Blazor.Client.Forms;

public enum ApiFailureKind
{
    // Field errors from problem details; a FormErrorMapper places them on the form
    FieldValidation,

    // One message for the whole form or a toast
    Message,

    // Nothing is shown: an authentication loss the navigation handling acts on, or a cancelled call
    Suppressed,

    // The antiforgery token was rejected; the page must be reloaded to obtain a new one
    AntiforgeryRecovery
}

// Message is null for FieldValidation and Suppressed
public sealed record ApiFailure(ApiFailureKind Kind, string? Message);

public static class ApiFailureClassifier
{
    public static ApiFailure Cancellation { get; } = new(ApiFailureKind.Suppressed, null);

    public static ApiFailure Classify(ApiCallResult result)
    {
        if (result.IsSuccess) throw new ArgumentException("A successful result has no failure to classify.", nameof(result));

        return Classify(result.Outcome, result.Problem);
    }

    public static ApiFailure Classify<TValue>(ApiCallResult<TValue> result)
    {
        if (result.IsSuccess) throw new ArgumentException("A successful result has no failure to classify.", nameof(result));

        return Classify(result.Outcome, result.Problem);
    }

    public static ApiFailure Classify(ApiCallOutcome outcome, ApiCallProblem problem)
    {
        if (outcome == ApiCallOutcome.Success) throw new ArgumentException("A successful call has no failure to classify.", nameof(outcome));

        if (outcome == ApiCallOutcome.Unauthorized || problem.StatusCode == 401) return new ApiFailure(ApiFailureKind.Suppressed, null);

        if (IsAntiforgeryFailure(problem)) return new ApiFailure(ApiFailureKind.AntiforgeryRecovery, CommonStrings.AntiforgeryRecovery);

        return outcome switch
        {
            ApiCallOutcome.TransportFailure => new ApiFailure(ApiFailureKind.Message, CommonStrings.TransportFailure),
            ApiCallOutcome.InvalidResponse => new ApiFailure(ApiFailureKind.Message, CommonStrings.InvalidResponse),
            _ when problem.Errors.Count > 0 => new ApiFailure(ApiFailureKind.FieldValidation, null),
            _ => new ApiFailure(ApiFailureKind.Message, GetMessage(problem))
        };
    }

    public static bool IsAntiforgeryFailure(ApiCallProblem problem)
    {
        return problem.StatusCode == 400 && problem.Title?.Contains("antiforgery", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string GetMessage(ApiCallProblem problem)
    {
        if (!string.IsNullOrWhiteSpace(problem.Detail)) return problem.Detail;
        if (!string.IsNullOrWhiteSpace(problem.Title)) return problem.Title;
        return problem.StatusCode is { } statusCode ? string.Format(CultureInfo.CurrentCulture, CommonStrings.RequestFailedWithStatus, statusCode) : CommonStrings.TransportFailure;
    }
}
