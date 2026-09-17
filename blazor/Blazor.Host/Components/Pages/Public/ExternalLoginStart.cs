using Account.Client;
using Blazor.Client;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Blazor.Host.Components.Pages.Public;

public enum ExternalLoginFlow
{
    Login,
    Signup
}

// The names are the account API's ExternalProviderType values, which the start route binds by name
public enum ExternalLoginProvider
{
    Google,
    Entra,
    MitId
}

// One provider button: a GET form whose submission is the document navigation to the account API's start endpoint, so the
// static page needs no runtime. Action is the endpoint path and Fields the query parameters the form submits.
public sealed record ExternalLoginStartForm(ExternalLoginProvider Provider, string Action, IReadOnlyList<KeyValuePair<string, string>> Fields)
{
    // The URL the browser navigates to when the form is submitted
    public string Url => $"{Action}?{string.Join('&', Fields.Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"))}";
}

// Which external providers the static login and signup pages offer and where each one starts. A provider is offered when
// the deployment's system feature flag is on; MitID is a login provider only, because an account can never be created
// with MitID. Hiding a button is presentation only: the account API decides whether a flow may start. Every start names
// this edition, so the callback's success and failure destinations stay under the path base, carries the page's culture
// for a signup, and a return path sanitised by the same rule the account API applies. A login also carries the preferred
// tenant a tenant switch remembered, the hint the email login verification sends; the account API only honours it for a
// tenant the user is an active member of.
public static class ExternalLoginStart
{
    public const string EditionParameter = "Edition";
    public const string LocaleParameter = "Locale";
    public const string ReturnPathParameter = "ReturnPath";
    public const string PreferredTenantIdParameter = "PreferredTenantId";
    public const string Edition = "Blazor";

    public static IReadOnlyList<ExternalLoginStartForm> Create(IReadOnlyDictionary<string, bool> systemFeatureFlags, ExternalLoginFlow flow, string locale, string? returnPath, TenantId? preferredTenantId = null)
    {
        return GetProviders(systemFeatureFlags, flow).Select(provider => CreateForm(provider, flow, locale, returnPath, preferredTenantId)).ToArray();
    }

    public static IEnumerable<ExternalLoginProvider> GetProviders(IReadOnlyDictionary<string, bool> systemFeatureFlags, ExternalLoginFlow flow)
    {
        if (IsEnabled(systemFeatureFlags, FeatureFlags.GoogleOauth)) yield return ExternalLoginProvider.Google;
        if (IsEnabled(systemFeatureFlags, FeatureFlags.EntraOauth)) yield return ExternalLoginProvider.Entra;
        if (flow == ExternalLoginFlow.Login && IsEnabled(systemFeatureFlags, FeatureFlags.MitIdLogin)) yield return ExternalLoginProvider.MitId;
    }

    public static ExternalLoginStartForm CreateForm(ExternalLoginProvider provider, ExternalLoginFlow flow, string locale, string? returnPath, TenantId? preferredTenantId = null)
    {
        var action = flow == ExternalLoginFlow.Login
            ? AccountApiRoutes.StartExternalLogin(provider.ToString())
            : AccountApiRoutes.StartExternalSignup(provider.ToString());
        List<KeyValuePair<string, string>> fields =
        [
            new(EditionParameter, Edition),
            new(LocaleParameter, locale),
            new(ReturnPathParameter, AppUrls.SanitizeReturnPath(returnPath))
        ];
        // The signup start takes no preferred tenant, because a signup creates its own
        if (flow == ExternalLoginFlow.Login && preferredTenantId is not null)
        {
            fields.Add(new KeyValuePair<string, string>(PreferredTenantIdParameter, PreferredTenant.Format(preferredTenantId)));
        }

        return new ExternalLoginStartForm(provider, action, fields);
    }

    private static bool IsEnabled(IReadOnlyDictionary<string, bool> systemFeatureFlags, FeatureFlagDefinition flag)
    {
        return systemFeatureFlags.TryGetValue(flag.Key, out var enabled) && enabled;
    }
}
