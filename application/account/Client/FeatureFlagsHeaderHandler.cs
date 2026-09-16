namespace Account.Client;

// The gateway reports the caller's evaluated feature flags in x-user-feature-flags on authenticated responses, so a toggle
// or a refreshed access token reaches the flag state without another bootstrap
public sealed class FeatureFlagsHeaderHandler(FeatureFlagState featureFlagState) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var headerValue = response.Headers.TryGetValues(AccountApiHeaders.UserFeatureFlags, out var values) ? string.Join(',', values) : null;
        featureFlagState.ApplyHeader(headerValue);
        return response;
    }
}
