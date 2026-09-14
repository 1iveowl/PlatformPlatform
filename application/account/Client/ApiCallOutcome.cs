namespace Account.Client;

public enum ApiCallOutcome
{
    // A 2xx response, with a readable body when the call returns a value
    Success,

    // A non-2xx response whose problem details carry field errors
    ValidationFailure,

    // A 401 response, which carries the x-unauthorized-reason header when the gateway rejected the session
    Unauthorized,

    // Any other non-2xx response
    Failure,

    // A 2xx response whose body cannot be read as the expected value
    InvalidResponse,

    // No response: the connection failed or the request timed out
    TransportFailure
}
