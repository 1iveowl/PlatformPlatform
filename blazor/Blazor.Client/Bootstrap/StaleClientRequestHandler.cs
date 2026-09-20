// The write gate of the version policy: a client outside the supported version window, or one whose fingerprinted asset
// set is no longer served, never sends a mutation. The request is answered locally with a precondition failure that
// ApiFailureClassifier turns into the reload prompt, so the surface that attempted the write shows the same prompt as an
// antiforgery rejection and the user's edits stay on the form until they choose to reload.
//
// Reads pass through, because an old client stays usable for reading, and so does logout: a stale client must always be
// able to end its session. The gate is a client-side rule about what this runtime is willing to send; the server keeps
// accepting what its contracts accept.
//
// Before a mutation is forwarded, both signals are read again through StaleClientRecheck, so a tab that has not navigated
// since a deployment learns here instead of sending the write. A runtime already known to be stale asks nothing more, and
// a re-check that cannot complete never blocks the write.

using System.Net;
using System.Text;
using System.Text.Json;
using Account.Client;
using SharedKernel.ApiResults;

namespace Blazor.Client.Bootstrap;

public sealed class StaleClientRequestHandler(ClientVersionState versionState, Func<CancellationToken, Task> recheckBeforeWriteAsync) : DelegatingHandler
{
    public const HttpStatusCode RefusalStatusCode = HttpStatusCode.PreconditionFailed;

    // Not shown to anyone: the classifier matches it, and the prompt's wording is a localized string
    public const string RefusalTitle = "The client version is outside the supported window";

    private static readonly JsonSerializerOptions JsonSerializerOptions = ApiJsonSerializerOptions.Create();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsGated(request)) return await base.SendAsync(request, cancellationToken);

        // One re-check per mutation, and none once this runtime is stale: nothing a later answer could say would let it
        // send the write again
        if (!versionState.IsStale) await recheckBeforeWriteAsync(cancellationToken);

        return versionState.IsStale ? CreateRefusal(request) : await base.SendAsync(request, cancellationToken);
    }

    private static bool IsGated(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options) return false;

        return request.RequestUri?.AbsolutePath != AccountApiRoutes.Logout;
    }

    private static HttpResponseMessage CreateRefusal(HttpRequestMessage request)
    {
        var problem = new ProblemDetailsResponse(null, RefusalTitle, (int)RefusalStatusCode, null, null, null);
        return new HttpResponseMessage(RefusalStatusCode)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(problem, JsonSerializerOptions), Encoding.UTF8, "application/problem+json")
        };
    }
}
