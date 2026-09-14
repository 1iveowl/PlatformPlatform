namespace SharedKernel.ApiResults;

/// <summary>
///     The error shape every failed API call returns (RFC 9457 problem details), as written by ApiResult: Title is the
///     status name, Detail the error message, and Errors maps a camelCase property name to its validation messages.
///     TraceId is present only when the exception, model-binding or antiforgery middleware produced the response.
/// </summary>
[PublicAPI]
public sealed record ProblemDetailsResponse(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    Dictionary<string, string[]>? Errors,
    string? TraceId
);
