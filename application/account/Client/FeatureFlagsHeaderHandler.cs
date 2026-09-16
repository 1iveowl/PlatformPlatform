namespace Account.Client;

// The gateway reports the caller's evaluated feature flags in x-user-feature-flags on authenticated responses, so a toggle
// or a refreshed access token reaches the flag state without another bootstrap. The flag generation is read before the
// request is sent, so a response that arrives after a tenant switch, a sign-in as someone else or a logout changes nothing.
// The bootstrap response is skipped: its body carries the same flags, and they reach the state only together with the
// identity and antiforgery token of a bootstrap the registrant accepts, never from a read it discards. The switch-tenant
// response is skipped too: the gateway evaluates its header from the new tenant's access token while the state still holds
// the old tenant's identity, so the new tenant's flags arrive only with the bootstrap that commits the new tenant.
public sealed class FeatureFlagsHeaderHandler(FeatureFlagState featureFlagState) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath is AccountApiRoutes.Bootstrap or AccountApiRoutes.SwitchTenant)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var generation = featureFlagState.Generation;
        var response = await base.SendAsync(request, cancellationToken);
        var headerValue = response.Headers.TryGetValues(AccountApiHeaders.UserFeatureFlags, out var values) ? string.Join(',', values) : null;
        featureFlagState.ApplyHeader(headerValue, generation);
        return response;
    }
}
