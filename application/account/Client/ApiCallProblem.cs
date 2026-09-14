namespace Account.Client;

// Why a call did not succeed. Errors maps each field key exactly as the API returned it (camelCase) to its messages and is
// empty unless the outcome is a validation failure. StatusCode is null when no response was received.
public sealed record ApiCallProblem(int? StatusCode, string? Title, string? Detail, IReadOnlyDictionary<string, string[]> Errors, string? UnauthorizedReason);
