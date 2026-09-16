---
paths: blazor/**/*.razor,blazor/**/*.cs
description: Rules for reading feature flags in Blazor through FeatureFlagState with the registry definitions, never string keys
---

# Feature Flags

How a Blazor component or service reads a feature flag. Evaluation stays on the server: the bootstrap contract carries the system-scope flags and the user's evaluated tenant and user flags, the `x-user-feature-flags` response header carries later changes, and `FeatureFlagState` in `application/account/Client` reflects what the server reported. A flag the server did not report is disabled.

## Implementation

1. Inject `FeatureFlagState` and call `IsEnabled(FeatureFlags.<Name>)` with the `FeatureFlagDefinition` field from `SharedKernel.FeatureFlags.FeatureFlags` (in `SharedKernel.Contracts`, which the client projects reference). Never pass a string key; the definition's `Key` is the portable identifier, and renaming a flag in the registry breaks every call site at compile time.
2. Rely on the registered chain to fill the state: `SessionState` accepts one bootstrap read at a time and commits it through `HttpBootstrapSource.Apply`, which replaces the flags with `Replace(bootstrap)` together with the identity and the antiforgery token before any notification, and clears them with `Replace(null)` when the authenticated surface is left; `FeatureFlagsHeaderHandler` reads `Generation` before sending and calls `ApplyHeader(value, generation)` on every response except the bootstrap's, so a header from a request sent before a tenant switch, another sign-in or a logout changes nothing. Never parse the header or the bootstrap dictionaries in a component.
3. Subscribe to `FeatureFlagState.Changed` when a component must re-render on a toggle, call `InvokeAsync(StateHasChanged)` from the handler and unsubscribe in `Dispose`. A flag change lags a toggle by up to the access token lifetime, and the header replaces the evaluated set only for an authenticated scope; the state ignores a header while `UserId` is null.
4. Gate markup only after the bootstrap read in the browser. The host's prerender adapter (`HostBootstrapSource`) returns the flags inside `BootstrapResponse` but does not initialize `FeatureFlagState`, so `IsEnabled` reports disabled during prerender; a component that must decide during prerender reads `bootstrap.SystemFeatureFlags` or `bootstrap.User.FeatureFlags` from the response it already holds.
5. Keep flag state per identity scope: the state records `UserId` and `TenantId` from the bootstrap, a tenant switch reads the bootstrap again and then leaves with a full document navigation, and a lost session resets the state. Never cache a flag value in a static field or across identities.
6. Use a system flag (`SystemFeatureFlag`) for a deployment capability such as an external login provider; it is evaluated from configuration and environment variables on the server and reaches the client through `BootstrapResponse.SystemFeatureFlags`. Tenant and user flags arrive in the user's evaluated list. Which subtype to use is a backend decision in the registry, not a client one.

## Examples

### Example 1 - Reading a Flag by Its Definition

```csharp
// ✅ DO: the registry field, not its key (blazor/Blazor.Tests/Client/WebAssemblyAccountApiTests.cs, with
// FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags)
var featureFlagState = services.GetRequiredService<FeatureFlagState>();
featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeTrue();

// ❌ DON'T: a string key does not compile against IsEnabled(FeatureFlagDefinition), and a dictionary lookup bypasses the scope rules
var enabled = bootstrap.SystemFeatureFlags["google-oauth"];
```

### Example 2 - The State Is Filled by the Chain

```csharp
// ✅ DO: an accepted bootstrap stores the token and replaces the flags; SessionState raises the notification after its own
// commit (blazor/Blazor.Client/Bootstrap/HttpBootstrapSource.cs)
public Action Apply(BootstrapResponse? bootstrap)
{
    if (bootstrap is null)
    {
        antiforgeryTokenSource.Clear();
    }
    else
    {
        antiforgeryTokenSource.Store(bootstrap.AntiforgeryToken);
    }

    return featureFlagState.Replace(bootstrap);
}

// ✅ DO: the header handler applies later changes for the identity the request was sent under
// (application/account/Client/FeatureFlagsHeaderHandler.cs)
var generation = featureFlagState.Generation;
var response = await base.SendAsync(request, cancellationToken);
var headerValue = response.Headers.TryGetValues(AccountApiHeaders.UserFeatureFlags, out var values) ? string.Join(',', values) : null;
featureFlagState.ApplyHeader(headerValue, generation);

// ❌ DON'T: read the header in a component
var flags = response.Headers.GetValues("x-user-feature-flags").First().Split(',');
```
