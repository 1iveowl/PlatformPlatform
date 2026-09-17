using Account.Features.FeatureFlags.Queries;
using Account.Features.FeatureFlags.Requests;

namespace Account.Client;

// The feature flags a tenant owner or a user may configure, and the two overrides that change them. Each override response
// carries the refreshed x-user-feature-flags header, which FeatureFlagsHeaderHandler applies to FeatureFlagState, so the
// evaluated flags this runtime holds follow the change without a reload.
public sealed class FeatureFlagsClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<TenantConfigurableFeatureFlagsResponse>> GetTenantConfigurableAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<TenantConfigurableFeatureFlagsResponse>(AccountApiRoutes.TenantConfigurableFeatureFlags, cancellationToken);
    }

    public Task<ApiCallResult<UserConfigurableFeatureFlagsResponse>> GetUserConfigurableAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<UserConfigurableFeatureFlagsResponse>(AccountApiRoutes.UserConfigurableFeatureFlags, cancellationToken);
    }

    public Task<ApiCallResult> SetTenantOverrideAsync(string flagKey, SetTenantFeatureFlagOwnerCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.SetTenantFeatureFlagOverride(flagKey), command, cancellationToken);
    }

    public Task<ApiCallResult> SetUserOverrideAsync(string flagKey, SetUserFeatureFlagCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.SetUserFeatureFlagOverride(flagKey), command, cancellationToken);
    }
}
