// What the header's tenant switcher shows, matching the React edition's TenantSwitcher: it is hidden for a user with one
// tenant, lists every tenant by name with the current one marked, and names a tenant without a name with a placeholder.

using Account.Features.Tenants.Queries;
using SharedKernel.Domain;

namespace Blazor.Client.Session;

public sealed record TenantSwitcherOption(TenantId TenantId, string Name, bool IsCurrent);

public static class TenantSwitcherOptions
{
    public static bool IsVisible(IReadOnlyCollection<TenantSwitcherOption> options)
    {
        return options.Count > 1;
    }

    public static TenantSwitcherOption[] Create(IEnumerable<TenantInfo> tenants, TenantId? currentTenantId)
    {
        return tenants.Select(tenant => new TenantSwitcherOption(tenant.TenantId, GetDisplayName(tenant.TenantName), tenant.TenantId == currentTenantId)).ToArray();
    }

    public static string GetDisplayName(string? tenantName)
    {
        return string.IsNullOrWhiteSpace(tenantName) ? AccountStrings.UnnamedAccount : tenantName;
    }
}
