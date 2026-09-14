using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharedKernel.ApiResults;

namespace Account.Client;

// Sends one request to the account API and turns the response into an ApiCallResult, so an HTTP status never escapes as an
// exception. A request is sent exactly once: nothing here retries, because a state-changing call such as completing a
// login or switching tenant must not be repeated. Cancellation by the caller propagates as OperationCanceledException.
internal sealed class AccountApiTransport(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = ApiJsonSerializerOptions.Create();

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    public Task<ApiCallResult<TResponse>> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        return SendAsync<TResponse>(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
    }

    public Task<ApiCallResult> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        return SendAsync(new HttpRequestMessage(method, path), cancellationToken);
    }

    public Task<ApiCallResult> SendAsync<TRequest>(HttpMethod method, string path, TRequest body, CancellationToken cancellationToken)
    {
        return SendAsync(CreateRequest(method, path, body), cancellationToken);
    }

    public Task<ApiCallResult<TResponse>> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest body, CancellationToken cancellationToken)
    {
        return SendAsync<TResponse>(CreateRequest(method, path, body), cancellationToken);
    }

    private static HttpRequestMessage CreateRequest<TRequest>(HttpMethod method, string path, TRequest body)
    {
        return new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: JsonSerializerOptions) };
    }

    private Task<ApiCallResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return SendAsync(request, (_, _) => Task.FromResult(ApiCallResult.Success()), ApiCallResult.Failed, cancellationToken);
    }

    private Task<ApiCallResult<TResponse>> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return SendAsync(request, ReadValueAsync<TResponse>, ApiCallResult<TResponse>.Failed, cancellationToken);
    }

    private async Task<TResult> SendAsync<TResult>(
        HttpRequestMessage request,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> readSuccess,
        Func<ApiCallOutcome, ApiCallProblem, TResult> createFailure,
        CancellationToken cancellationToken
    )
    {
        using var requestMessage = request;
        try
        {
            using var response = await httpClient.SendAsync(requestMessage, cancellationToken);
            if (response.IsSuccessStatusCode) return await readSuccess(response, cancellationToken);

            var (outcome, problem) = await ReadProblemAsync(response, cancellationToken);
            return createFailure(outcome, problem);
        }
        catch (HttpRequestException exception)
        {
            return createFailure(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, exception.Message, NoErrors, null));
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation the caller did not ask for
            return createFailure(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, exception.Message, NoErrors, null));
        }
    }

    private static async Task<ApiCallResult<TResponse>> ReadValueAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var value = TryDeserialize<TResponse>(body);
        return value is null
            ? ApiCallResult<TResponse>.Failed(ApiCallOutcome.InvalidResponse, new ApiCallProblem((int)response.StatusCode, response.ReasonPhrase, null, NoErrors, null))
            : ApiCallResult<TResponse>.Success(value);
    }

    private static async Task<(ApiCallOutcome Outcome, ApiCallProblem Problem)> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var problemDetails = TryDeserialize<ProblemDetailsResponse>(body);
        var statusCode = (int)response.StatusCode;
        var title = problemDetails?.Title ?? response.ReasonPhrase;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var unauthorizedReason = response.Headers.TryGetValues(AccountApiHeaders.UnauthorizedReason, out var values) ? values.FirstOrDefault() : null;
            return (ApiCallOutcome.Unauthorized, new ApiCallProblem(statusCode, title, problemDetails?.Detail, NoErrors, unauthorizedReason));
        }

        if (problemDetails?.Errors is { Count: > 0 } errors)
        {
            return (ApiCallOutcome.ValidationFailure, new ApiCallProblem(statusCode, title, problemDetails.Detail, errors, null));
        }

        return (ApiCallOutcome.Failure, new ApiCallProblem(statusCode, title, problemDetails?.Detail, NoErrors, null));
    }

    private static TValue? TryDeserialize<TValue>(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return default;

        try
        {
            return JsonSerializer.Deserialize<TValue>(body, JsonSerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
