---
paths: blazor/**/*.razor,blazor/**/*.cs,application/account/Client/**
description: Rules for calling the account API from Blazor components through the typed clients, handling results, cancellation and 401 responses
---

# API Access

The typed clients in `application/account/Client` are the only way a Blazor component or service reaches the account API. A component injects a client, passes contract types and a cancellation token, and branches on the `ApiCallResult` it gets back. No component builds a URL, reads JSON or handles a 401.

## Implementation

1. Inject the typed client for the area (`EmailAuthenticationClient`, `AuthenticationClient`, `UsersClient`, `TenantsClient`) and call its method with request records from `Account.Contracts` and a required `CancellationToken`: `HttpContext.RequestAborted` in a static form handler, `SessionState.RequestsAborted` through `Session.UnlessLeavingAsync` (step 6), the component's own token or `CancellationToken.None` in the browser.
2. Branch on `ApiCallResult` or `ApiCallResult<T>`: `IsSuccess` gives `Value`; otherwise `Outcome` is one of `ValidationFailure`, `Unauthorized`, `Failure`, `InvalidResponse` or `TransportFailure` and `Problem` carries the status, title, detail, field errors (camelCase keys as returned) and the unauthorized reason. Never throw on a failed result and never wrap the client in a facade that does.
3. Add an endpoint by adding a contract in `Account.Contracts`, a path in `AccountApiRoutes` (constants for fixed paths, methods that escape route and query values) and a method on the area's client over `AccountApiTransport`. Never write `/api/account/...` or call `HttpClient`, `GetFromJsonAsync` or `JsonSerializer` in a component or a Blazor service.
4. Show a failure through the shared presentation: `FormErrorMapper.ApplyFailure(result)` on a static form, `ApiFailurePresenter.Present(result, formErrors)` on an interactive surface. Return early after presenting.
5. Leave 401 to the handler chain: in the browser `UnauthorizedResponseHandler` calls `AuthenticationNavigator.LeaveForUnauthorized`, which leaves the runtime once with a full document navigation to login or to the error page named by `x-unauthorized-reason`. A component that receives `ApiCallOutcome.Unauthorized` returns without showing anything. A component that reads the bootstrap and finds `!IsAuthenticated` while interactive calls `LeaveForLogin()`.
6. Read the session and make calls that must end with it through the departure-aware forms on `SessionState`: `GetUnlessLeavingAsync()`, `RefreshUnlessLeavingAsync()` and `UnlessLeavingAsync(cancellationToken => client.MethodAsync(..., cancellationToken))`, which passes `RequestsAborted`. Leaving the authenticated surface (logout, tenant switch, a 401) cancels the bootstrap read and every call on that token, and each form returns null for it, also for a result that arrives after the departure. On null the component returns at once: it renders no identity, presents no failure, navigates nowhere and never retries, because `AuthenticationNavigator` has already started the full document navigation. Never await `GetAsync`, `RefreshAsync` or a call on `RequestsAborted` directly in a component, and never catch `OperationCanceledException` there; an unhandled cancellation reveals the framework's error banner until the next document loads.
7. Send state-changing calls through the registered chain, which adds the antiforgery token from the latest bootstrap, the UI culture as `X-Locale` and applies the `x-user-feature-flags` response header to `FeatureFlagState`. Never add these headers by hand.
8. Register the clients in the host with `HostAccountApiHandler` (server to server, direct to `ACCOUNT_API_URL`), which relays the current request's bearer token, antiforgery pair, locale and client address on every send and copies the token headers and `Set-Cookie` back. The handler is pooled, so it holds nothing per user; read everything from `HttpContext` inside `SendAsync`.
9. Resolve a client lazily through `IServiceProvider` in a component that uses it only after the runtime starts, so prerendering never constructs it; inject it directly when a static form handler or prerender calls it.
10. Do not retry: a timeout is a `TransportFailure`, and a cancellation by the caller is rethrown as `OperationCanceledException`.

## Examples

### Example 1 - A Static Form Handler

```csharp
// ✅ DO: contract in, result out, the mapper shows the failure (blazor/Blazor.Host/Components/Pages/Public/Login.razor)
private async Task StartAsync()
{
    var result = await EmailAuthentication.StartLoginAsync(new StartEmailLoginCommand(Input.Email), HttpContext.RequestAborted);
    if (!result.IsSuccess)
    {
        _formErrors.ApplyFailure(result);
        return;
    }

    Navigation.NavigateTo(
        $"{AppUrls.ToAbsolute("login/verify")}?id={Uri.EscapeDataString(result.Value.EmailLoginId.Value)}&email={Uri.EscapeDataString(Input.Email)}&returnPath={Uri.EscapeDataString(AppUrls.SanitizeReturnPath(ReturnPath))}"
    );
}
```

### Example 2 - An Interactive Read and Write

```csharp
// ✅ DO: a 401 is handled by the chain, so a failed read returns quietly (blazor/Blazor.Client/AuthenticatedApp.razor)
private async Task LoadTenantsAsync()
{
    var result = await GetService<TenantsClient>().GetTenantsAsync(CancellationToken.None);
    if (!result.IsSuccess) return;

    _tenants = result.Value.Tenants.Select(tenant => new TenantOption(tenant.TenantId, tenant.TenantName)).ToArray();
}

// ✅ DO: a route with escaped values lives in the client project (application/account/Client/AccountApiRoutes.cs)
public static string ChangeUserRole(UserId userId)
{
    return $"{User(userId)}/change-user-role";
}
```

```csharp
// ✅ DO: a departure returns null and the component stops (blazor/Blazor.Client/Profile/ProfileSurface.razor)
var bootstrap = await Session.GetUnlessLeavingAsync();
if (bootstrap is null || !RendererInfo.IsInteractive) return;

var result = await Session.UnlessLeavingAsync(Services.GetRequiredService<UsersClient>().GetCurrentUserAsync);
if (result is null) return;

// ❌ DON'T: a read or call cancelled by leaving the surface throws out of the component and shows the error banner
var bootstrap = await Session.GetAsync();
var result = await Services.GetRequiredService<UsersClient>().GetCurrentUserAsync(Session.RequestsAborted);
```

```csharp
// ❌ DON'T: a URL string and JSON handling in a component
var users = await Http.GetFromJsonAsync<UsersResponse>($"/api/account/users?Search={search}&PageSize=25");

// ❌ DON'T: a facade that turns results into exceptions and catches them in the component
try { await UsersApi.ChangeRoleAsync(id, role); } catch (UsersApiException exception) { _dialogError = exception.Message; }

// ❌ DON'T: handle a 401 in a component; the handler chain already leaves the runtime
if (result.Outcome == ApiCallOutcome.Unauthorized) Navigation.NavigateTo("/blazor/login", true);
```
