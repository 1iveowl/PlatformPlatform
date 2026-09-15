// Canned account API failures for the Development-only form error fixture pages, shared by the static server-rendered case
// in the host and the interactive WebAssembly case. The messages are fixed English text and carry no user data.

using System.ComponentModel.DataAnnotations;
using Account.Client;

namespace Blazor.Client.Development;

public static class FormErrorScenarios
{
    public const string FieldMessages = "field-messages";
    public const string CaseInsensitiveKey = "case-insensitive-key";
    public const string UnmatchedKey = "unmatched-key";
    public const string Detail = "detail";
    public const string TitleFallback = "title-fallback";
    public const string Antiforgery = "antiforgery";
    public const string Unauthorized = "unauthorized";
    public const string TransportFailure = "transport-failure";

    public const string NameTooShortMessage = "Name must be at least 3 characters.";
    public const string NameReservedMessage = "Name is reserved.";
    public const string EmailTakenMessage = "Email <b>is</b> already in use.";
    public const string TenantLockedMessage = "The tenant is locked.";
    public const string ConflictDetail = "The user was changed by someone else.";
    public const string ConflictTitle = "Conflict";
    public const string UnauthorizedDetail = "The session has expired.";

    public static readonly string[] All = [FieldMessages, CaseInsensitiveKey, UnmatchedKey, Detail, TitleFallback, Antiforgery, Unauthorized, TransportFailure];

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    public static (ApiCallOutcome Outcome, ApiCallProblem Problem) Get(string scenario)
    {
        return scenario switch
        {
            FieldMessages => Validation(new Dictionary<string, string[]> { ["name"] = [NameTooShortMessage, NameReservedMessage], ["email"] = [EmailTakenMessage] }),
            CaseInsensitiveKey => Validation(new Dictionary<string, string[]> { ["EMAIL"] = [EmailTakenMessage] }),
            UnmatchedKey => Validation(new Dictionary<string, string[]> { ["tenantSlug"] = [TenantLockedMessage], ["name"] = [NameReservedMessage] }),
            Detail => (ApiCallOutcome.Failure, new ApiCallProblem(409, ConflictTitle, ConflictDetail, NoErrors, null)),
            TitleFallback => (ApiCallOutcome.Failure, new ApiCallProblem(409, ConflictTitle, null, NoErrors, null)),
            Antiforgery => (ApiCallOutcome.Failure, new ApiCallProblem(400, "Antiforgery token validation failed", "The antiforgery token was not accepted.", NoErrors, null)),
            Unauthorized => (ApiCallOutcome.Unauthorized, new ApiCallProblem(401, "Unauthorized", UnauthorizedDetail, NoErrors, null)),
            TransportFailure => (ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, "Connection refused", NoErrors, null)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown form error scenario.")
        };
    }

    private static (ApiCallOutcome Outcome, ApiCallProblem Problem) Validation(Dictionary<string, string[]> errors)
    {
        return (ApiCallOutcome.ValidationFailure, new ApiCallProblem(400, "One or more validation errors occurred.", null, errors, null));
    }
}

public sealed class FormErrorFixtureForm
{
    [Required]
    public string Name { get; set; } = "";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    public string Scenario { get; set; } = FormErrorScenarios.FieldMessages;
}
