---
paths: blazor/**/*.razor,blazor/**/Program.cs
description: Rules for choosing the render mode of each Blazor surface, and for navigation and services across the boundary
---

# Render Modes

Rules for which Blazor surfaces are server-rendered and which are interactive, and why. Written from the stage B2 spike
(Linear EP-55) for the rules corpus; stage C productionises the arrangement and may amend it.

## Implementation

1. Serve the public surface with static server-side rendering and no render mode:
   - Public means every page reachable without a session: landing, login, signup, one-time-password verification and
     legal pages
   - Why: these pages are the first load for a new visitor, and static rendering starts no WebAssembly download. A
     WebAssembly runtime of several megabytes is acceptable behind login and not on the first page a visitor sees
   - Never register or use `InteractiveServer` for public pages: it needs a circuit per visitor, and static rendering
     already gives forms, validation and enhanced navigation

2. Build public forms as static SSR forms that post to the host:
   - `EditForm` with a unique `FormName`, `[SupplyParameterFromForm]` and `DataAnnotationsValidator`; the host validates
     the antiforgery token on every form post
   - The form handler calls the API server to server and forwards the posted antiforgery token as the `x-xsrf-token`
     header with the antiforgery cookie, so the API still enforces its own antiforgery check
   - Copy the API's authentication token headers (`x-refresh-token`, `x-access-token`) and any `Set-Cookie` onto the
     host's response; the gateway turns the token headers into the session cookies, as it does for API calls
   - Why: the browser never holds a token in script, and the gateway stays the only place session cookies are written

3. Serve the authenticated surface as server-rendered pages that each host a WebAssembly component:
   - Put `@rendermode InteractiveWebAssembly` on the component a page hosts, never on `<Routes>` or the document root
   - Mark such pages so the host page renders the runtime preload links only for them; render the import map on every
     page so enhanced navigation can still start the runtime
   - Why: a render mode on the document root with prerendering disabled renders nothing when the runtime fails to boot,
     no markup and no error, which removes the diagnostic signal. A server-rendered shell keeps the page visible

4. Keep one router and plain links:
   - All routes, public and authenticated, are Razor component endpoints on the host's one `Router`; there is no
     interactive router
   - Links are plain `<a href>` elements with absolute paths under the path base. Under `base-uri 'none'` the browser
     resolves a non-enhanced link against the document URL, so a relative href breaks below the first path segment
   - Inside one surface, let enhanced navigation handle links and enhanced forms: the document and a started runtime stay
   - Cross the authentication boundary with a full document navigation, in both directions:
     - Verification forms that sign in are not enhanced, so the browser follows the redirect with a full load
     - Links from the public surface into the authenticated one, and back, carry `data-enhance-nav="false"`
     - Logout and a lost session navigate with `forceLoad: true`
   - Why: a fresh document on sign-in gets the preload links and the session cookies from the redirect; a fresh
     document after sign-out leaves no runtime and no user state in memory

5. Resolve services for server rendering from the host container:
   - Every service a component injects during static rendering or prerendering must be registered in `Blazor.Host`,
     including component library services such as `AddFluentUIComponents()`
   - Where the two sides obtain data differently, define the interface in `Blazor.Client` and register a host
     implementation and a client implementation. The host implementation reads the request; the client implementation
     calls an endpoint
   - Why: a missing host registration fails the whole request with HTTP 500 naming the missing service, not a blank page

6. Split authentication state by render mode:
   - The host obtains identity per request from the bearer token the gateway derives from the session cookies, and the
     locale from the token's locale claim or, for an anonymous visitor, from `Accept-Language`
   - The WebAssembly client obtains identity, runtime configuration, system-scope feature flags and the antiforgery
     request token from the bootstrap endpoint, and reads it again after a tenant switch
   - When the bootstrap reports no session, or an API call returns 401, the client navigates to login with a full load

## Examples

```razor
@* ✅ DO: authenticated page is server-rendered; the render mode is on the component it hosts *@
@page "/app"
@attribute [Authorize]
@attribute [InteractiveSurface]

<AuthenticatedApp @rendermode="InteractiveWebAssembly"/>

@* ❌ DON'T: render mode on the router makes every page, public ones included, a WebAssembly island *@
<Routes @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)"/>
```

```razor
@* ✅ DO: the sign-in verification form is not enhanced, so success is a full document navigation *@
<EditForm Model="Input" FormName="login-verify" OnValidSubmit="CompleteAsync">

@* ✅ DO: the link into the authenticated surface opts out of enhanced navigation and is absolute *@
<a href="/blazor/app" data-enhance-nav="false">Open the app</a>

@* ❌ DON'T: a relative href resolves against the document URL under base-uri 'none' *@
<a href="app">Open the app</a>
```
