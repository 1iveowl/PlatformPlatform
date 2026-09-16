// Keeps the previous identity's work from outliving a session transition. While a logout or tenant switch is in flight,
// every state-changing call other than the transition's own is refused before it is sent. Once the session has ended
// (the transition's own request succeeded, or AuthenticationNavigator is leaving), every call is refused before it is sent
// and every response that arrives afterwards is discarded before the feature flag, unauthorized and typed-client layers
// above this handler see it, so a delayed response cannot repopulate the surface or choose another destination.
// A refusal is an HttpRequestException, which the transport reports as a TransportFailure; nothing is retried.

namespace Blazor.Client.Bootstrap;

public sealed class SessionTransitionHandler(SessionTransitionGate gate, AuthenticationNavigator authenticationNavigator) : DelegatingHandler
{
    private bool IsSessionOver => gate.IsIdentityReplaced || authenticationNavigator.IsLeaving;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsSessionOver) throw new HttpRequestException("The session of this browser runtime has ended.");

        var isTransitionRequest = gate.IsTransitionRequest(request);
        if (gate.IsActive && !isTransitionRequest && !IsSafe(request.Method))
        {
            throw new HttpRequestException("A logout or tenant switch is in progress.");
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (isTransitionRequest)
        {
            if (response.IsSuccessStatusCode) gate.MarkIdentityReplaced();
            return response;
        }

        if (!IsSessionOver) return response;

        response.Dispose();
        throw new HttpRequestException("The session of this browser runtime ended while the request was in flight.");
    }

    private static bool IsSafe(HttpMethod method)
    {
        return method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;
    }
}
