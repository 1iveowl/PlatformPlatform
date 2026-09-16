---
paths: blazor/**/*.razor,blazor/**/HostApplication.cs,blazor/**/Program.cs,blazor/**/Bootstrap/*.cs,blazor/**/Shell/*.cs
description: Rules for which Blazor surfaces are server-rendered and which are interactive, and for navigation, services and identity across that boundary
---

# Render Modes

Rules for which Blazor surfaces are static server-rendered and which are interactive WebAssembly, how navigation crosses the authentication boundary, and how services and identity are resolved on each side. The public surface starts no WebAssembly download; the authenticated surface is prerendered by the host and then started in the browser.

## Implementation

1. Serve the public surface with static server-side rendering and no render mode:
   - Public means every page reachable without a session: landing, login, signup, one-time-password verification and legal pages
   - Never register or use `InteractiveServer`; static rendering already gives forms, validation and enhanced navigation
   - Public pages carry no `[InteractiveSurface]`, so the host page renders no runtime preload links for them; the import map is on every page so enhanced navigation can still start the runtime later
2. Build public forms as static forms that post to the host (see the forms-and-validation rule for the full pattern):
   - `EditForm` with a unique `FormName`, `[SupplyParameterFromForm(FormName = ...)]` and `DataAnnotationsValidator`
   - The handler calls the account API server to server through the typed client; `HostAccountApiHandler` forwards the posted antiforgery token as `x-xsrf-token` with the antiforgery cookie and copies `x-refresh-token`, `x-access-token`, `x-refresh-authentication-tokens-required` and `Set-Cookie` onto the host response, which the gateway turns into the session cookies
3. Serve the authenticated surface as server-rendered pages that each host one WebAssembly component:
   - Put `@rendermode="InteractiveWebAssembly"` on the component the page hosts, never on `<Routes>` or the document root
   - Mark the page with `[Authorize]` and `[InteractiveSurface]`, so the host page renders the runtime preload links only there
   - Prerender a component whose content is data behind login that it fetches itself, such as a list: during prerender it renders its frame (heading, search, the list's loading state) and no user data, and it loads the data only once `RendererInfo.IsInteractive`, as `DataList` does. Use `new InteractiveWebAssemblyRenderMode(false)` only for a component that has no meaningful frame without its data
4. Keep one router and plain links:
   - All routes, public and authenticated, are Razor component endpoints on the host's one `Router` in `Routes.razor`; there is no interactive router
   - Links are plain `<a href>` elements whose paths are built with `AppUrls.ToAbsolute(...)` or `AppUrls.AuthenticatedHome`, never a literal `/blazor/...`; `base-uri 'none'` makes the browser ignore `<base href>`, so a relative href breaks below the first path segment
   - Inside one surface, let enhanced navigation handle links and enhanced forms
   - Cross the authentication boundary with a full document navigation, in both directions: the verification forms that sign in are not enhanced; links between the surfaces carry `data-enhance-nav="false"`; logout, tenant switch and a lost session leave through `AuthenticationNavigator`, which navigates with `forceLoad: true` once and raises `Leaving` first
5. Register every service a component injects during static rendering or prerendering in `Blazor.Host/HostApplication.cs` as well as in `Blazor.Client/Program.cs`, including `AddFluentUIComponents`, `ToastService`, `ApiFailurePresenter` and `FeatureFlagState`. Where the two sides obtain data differently, define the interface in `Blazor.Client` and register a host implementation and a client implementation (`IBootstrapSource`). A missing host registration fails the request with HTTP 500 naming the service.
6. Keep host state request-scoped: the host reads the bearer token, the antiforgery pair, the locale and the client address from the current `HttpContext` on every use (`HostAccountApiHandler`, `HostBootstrapSource`, `HostShell.GetNonce`). A singleton such as `HostShell` holds only values that are the same for every visitor.
7. Split identity by render mode:
   - The host obtains identity per request from the bearer token the gateway derives from the session cookies, and the locale from the token's `locale` claim or, for an anonymous visitor, from `Accept-Language`
   - The WebAssembly client obtains identity, runtime configuration, system-scope feature flags and the antiforgery token from the bootstrap endpoint through `IBootstrapSource`, and reads it again after a tenant switch
   - A component that finds `!bootstrap.IsAuthenticated` while `RendererInfo.IsInteractive` calls `AuthenticationNavigator.LeaveForLogin()`; during prerender it renders what the host adapter returned
8. Treat the host page as a personalized document, not a reusable shell template: it carries a per-request nonce and an authenticated page prerenders for the requesting user, so `HostShell.ApplyPageHeadersAsync` serves every component page with `no-store`. Nothing that could be shared between visitors or cached offline may render user data into the document; identity reaches the client through the bootstrap endpoint.

## Examples

### Example 1 - Render Mode on the Hosted Component

```razor
@* ✅ DO: the page is server-rendered; the render mode is on the component it hosts (blazor/Blazor.Host/Components/Pages/App/AppHome.razor) *@
@page "/app"
@using Blazor.Host.Shell
@using Microsoft.AspNetCore.Authorization
@attribute [Authorize]
@attribute [InteractiveSurface]

<PageTitle>@AccountStrings.Workspace</PageTitle>

<AuthenticatedApp Page="home" @rendermode="InteractiveWebAssembly"/>

@* ✅ DO: a prerendered list frame; the data loads in the browser (blazor/Blazor.Host/Components/Pages/App/UsersPage.razor) *@
<UsersSurface @rendermode="InteractiveWebAssembly"/>

@* ❌ DON'T: a render mode on the router makes every page, public ones included, a WebAssembly island *@
<Routes @rendermode="new InteractiveWebAssemblyRenderMode(false)"/>
```

### Example 2 - Links and the Authentication Boundary

```razor
@* ✅ DO: absolute paths through AppUrls; the link into the authenticated surface opts out of enhanced navigation
   (blazor/Blazor.Host/Components/Layout/PublicNav.razor) *@
<a href="@AppUrls.ToAbsolute("login")" data-testid="nav-login">@CommonStrings.LogIn</a> |
<a href="@AppUrls.AuthenticatedHome" data-enhance-nav="false" data-testid="nav-app">@AuthenticationStrings.OpenTheApp</a>

@* ❌ DON'T: a literal path base, or a relative href that resolves against the document URL under base-uri 'none' *@
<a href="/blazor/app">Open the app</a>
<a href="app">Open the app</a>
```

```csharp
// ✅ DO: every way out of the authenticated surface is one full document navigation (blazor/Blazor.Client/Bootstrap/AuthenticationNavigator.cs)
private void Leave(string url)
{
    if (IsLeaving) return;
    IsLeaving = true;
    Leaving?.Invoke();
    navigationManager.NavigateTo(url, true);
}

// ❌ DON'T: an enhanced navigation after logout keeps the runtime and the previous identity's state in memory
navigationManager.NavigateTo("/blazor/login");
```
