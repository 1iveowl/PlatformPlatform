// Form models for the static server-rendered public surface.

using System.ComponentModel.DataAnnotations;
using Account.Client;

namespace Blazor.Host.Components.Pages.Public;

public sealed class EmailForm
{
    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = "";
}

public sealed class OneTimePasswordForm
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string OneTimePassword { get; set; } = "";
}

// Transitional: the form error mapper task replaces this with the shared mapping of an ApiCallProblem to form errors
public static class PublicFormErrors
{
    extension(ApiCallProblem problem)
    {
        // The field errors joined, else the detail, else the title. A call that received no error response (the account API
        // was unreachable, or a success body could not be read) is not a form error and fails the request, as it did before
        // the typed clients.
        public string GetFormErrorMessage()
        {
            if (problem.StatusCode is not { } statusCode || statusCode is >= 200 and < 300)
            {
                throw new InvalidOperationException($"The account API call returned no error response: {problem.Detail ?? problem.Title}.");
            }

            var messages = problem.Errors.Values.SelectMany(fieldMessages => fieldMessages).ToArray();
            if (messages.Length > 0) return string.Join(" ", messages);

            if (problem.Detail is { Length: > 0 } detail) return detail;
            return problem.Title ?? $"Request failed with status {statusCode}.";
        }
    }
}
