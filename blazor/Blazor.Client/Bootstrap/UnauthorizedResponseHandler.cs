// Any 401 from an API path leaves the runtime: the gateway answers API paths with 401 and x-unauthorized-reason when the
// session behind the cookies is revoked or gone, and the access token it derives from a valid session is never
// rejected, so a 401 always means the session is over.

using System.Net;

namespace Blazor.Client.Bootstrap;

public sealed class UnauthorizedResponseHandler(AuthenticationNavigator authenticationNavigator) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.RequestUri?.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) != true)
        {
            return response;
        }

        var unauthorizedReason = response.Headers.TryGetValues(AuthenticationNavigator.UnauthorizedReasonHeaderName, out var values) ? values.FirstOrDefault() : null;
        authenticationNavigator.LeaveForUnauthorized(unauthorizedReason);
        return response;
    }
}
