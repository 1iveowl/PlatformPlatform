// Spike code (Blazor edition, stage B2): the throwaway bootstrap contract that both render modes read. Stage C session
// C3 builds the real endpoint; the shape it demonstrates is identity, runtime configuration and system-scope feature flags.

namespace Blazor.Client.Bootstrap;

public sealed record BootstrapResponse(
    bool IsAuthenticated,
    BootstrapUser? User,
    string Locale,
    IReadOnlyDictionary<string, string> RuntimeConfiguration,
    IReadOnlyDictionary<string, bool> SystemFeatureFlags,
    string AntiforgeryToken,
    string Source
);

public sealed record BootstrapUser(
    string? Id,
    string? Email,
    string? TenantId,
    string? TenantName,
    string? Role,
    string? SessionId
);

public interface IBootstrapSource
{
    Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default);
}
