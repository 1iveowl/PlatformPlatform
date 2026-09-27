# Back-office hosting and identity, Blazor edition

Where the Blazor back office is served, how a back-office identity reaches its pages, and what a deployment must
provide. Decided by the stage G spike (EP-202, milestone G0) and judged by the G0 security review before any back-office
surface is built on it.

Verified at `e5cb68c01` plus the change that added this record, 2026-09-27, against the local stack: the host tests in
`blazor/Blazor.Tests/Account/HostSecurityTests.BackOffice.cs`, the typed-client tests in
`blazor/Blazor.Tests/Client/BackOffice/BackOfficeClientTests.cs`, `e2e --blazor back-office-flows` (admin and user through
the mock, the admin write with the antiforgery check on, the user's write refused with 403) and the `backOffice` case of
`blazor-harness shell-policy`. Nothing here was observed in Azure; every Azure statement is marked as an assumption.

## Decision

The account API's back-office listener forwards the Blazor back-office paths to the internal Blazor host, and the
back-office identity travels with the request in a header protected by the shared data protection key ring
(option b of the issue, with a protected identity in place of the raw principal headers).

- The back office is served under the path base `/blazor` on the back-office host, so the React back office keeps the
  host's root. The placeholder page is `/blazor/back-office`.
- The platform authentication (Easy Auth in Azure, `MockEasyAuthMiddleware` locally, Development only) stays in front of
  the back-office host, unchanged.
- `application/account/Api/BackOfficeBlazorProxy.cs` maps `/blazor/{**catch-all}` on the back-office host only, with
  `RequireAuthorization(BackOfficePolicy)`. An unauthenticated page request is redirected to the platform login by the
  account API's own back-office handler before anything is forwarded. The route asks the `BackOfficeAdmin` policy for its
  verdict, protects the authenticated claims and that verdict with `ForwardedBackOfficeIdentity`
  (`application/shared-kernel/SharedKernel.Security/Authentication/BackOfficeIdentity/`), and forwards with YARP's
  `IHttpForwarder` to `BACK_OFFICE_BLAZOR_HOST_URL`. It removes the `X-MS-CLIENT-PRINCIPAL*` headers, any inbound
  `X-Back-Office-Identity` and every inbound `X-Forwarded-*` header, then sets `X-Back-Office-Identity`,
  `X-Forwarded-Host` (the back-office host the route matched), `X-Forwarded-Proto` and `X-Forwarded-For`.
- The Blazor host authenticates back-office pages with its own scheme (`blazor/Blazor.Host/Account/BackOfficeAuthentication.cs`),
  which reads only `X-Back-Office-Identity`, only on the back-office host, and never the principal headers. A back-office
  page carries `[Authorize(Policy = "BackOffice")]` and `[BackOfficeSurface]`.
- The account API's back-office authentication handler and its policies did not change. `AntiforgeryMiddleware` did not
  change.

## Rejected options

- **(a) The Blazor host answers the back-office host name and reads the principal headers itself.** In Azure the
  back-office domain is bound to the `back-office` container app (`cloud-infrastructure/cluster/main-cluster.bicep`); a
  Blazor host answering it would need a second container app on another domain with its own Easy Auth (so the pages would
  not be on the back-office host, and the API calls would cross origins without the back-office Easy Auth session), or a
  proxy anyway. Reading the principal headers at the Blazor host would also trust them from any caller that reaches its
  internal ingress, the app gateway included, which the business rules forbid.
- **(b) as written: forward the raw principal headers.** The Blazor host could tell the back-office listener from the app
  gateway or any other caller in the environment only by network position, and both arrive from loopback locally and
  from the Container Apps envoy (100.64.0.0/10) in Azure. The protected identity needs the key ring instead.
- **(c) A back-office route in the app gateway.** Back-office traffic bypasses the gateway by design (the AppHost comment
  at `BACK_OFFICE_KESTREL_PORT`), and the gateway has no platform authentication in front of it. Rejected as a reversal of
  that topology.
- **(d) The Blazor host calls the back-office API server to server.** The back-office container app is behind Easy Auth,
  so a server-to-server call would need a token the platform does not issue, or a forged `Host` against the internal
  account-api app. The browser calls the back-office API itself instead, same origin, through Easy Auth.

## Request paths

- **Back-office page.** Browser, `https://<back-office host>/blazor/back-office`, then the platform authentication, then
  the account API's back-office listener (back-office policy, admin verdict, protected identity), then the Blazor host at
  `BACK_OFFICE_BLAZOR_HOST_URL` (forwarded headers middleware accepts the back-office host, `BackOfficeSurface` admits only
  back-office pages there, the `BackOffice` scheme rebuilds the identity), which renders the page with the name and the
  admin marker and a WebAssembly island.
- **Back-office API call.** The island's `BackOfficeClient` (`application/account/Client/BackOfficeClient.cs`) calls
  `/api/back-office/*` on the same origin, through the platform authentication, to the account API's back-office
  endpoints, exactly as the React back office does. `GET /api/back-office/me` and
  `PUT /api/back-office/tenants/{id}/ab-inclusion-pin` are the two calls the spike makes.

## Trust boundary for the principal headers

- The `X-MS-CLIENT-PRINCIPAL*` headers are trusted only by the account API behind the platform authentication, as before.
  The Blazor host never reads them. A request that arrives through the app gateway, on the app host, with a forged
  `X-Forwarded-Host` naming the back-office host, or straight to the Blazor host's port under the back-office name, carries
  no back-office identity: each is a host test that expects the redirect to the platform login or a 404.
- `X-Back-Office-Identity` is valid only when this deployment's key ring protected it for its purpose string within the
  last minute (`ForwardedBackOfficeIdentity.Lifetime`). It is created per request and never returned to the browser.
- Trust-boundary change: `HostApplication.CreateForwardedHeadersOptions` now also allows the `BACK_OFFICE_PUBLIC_URL` host
  in `X-Forwarded-Host`. Known networks and the forward limit are unchanged. Naming that host grants nothing by itself:
  back-office pages still need the protected identity, and every other page answers 404 there.
- Observed and left as it was, for the G0 review: the account API's `BackOfficeIdentityHandler` trusts the principal
  headers from any caller, and no file under `application/AppGateway/` removes them. The back-office endpoints require the
  back-office host, so the gateway's app-host traffic does not match them today.

## Antiforgery

- The Blazor host issues the token when it renders a back-office page, for the identity it rebuilt from the protected
  header. The claims are the account API handler's own, so the name identifier claim (the principal id) and therefore the
  antiforgery claim identifier are the same in both processes, which share the key ring
  (`AddCrossServiceDataProtection`).
- The cookie is `__Host-xsrf-token`, set by the Blazor host's response and relayed by the forwarder, host-only to the
  back-office host. The request token reaches the WebAssembly island through the framework's `AntiforgeryStateProvider`
  (the persisted component state of the prerendered document); `BackOfficeAntiforgeryTokenSource` puts it in
  `x-xsrf-token` on state-changing calls only.
- A page on the app host can neither obtain nor use it: app pages are rendered for the app user's token principal, so any
  token they carry is bound to another identity; the back-office cookie is host-only and never sent with an app-host
  request; the back-office request token exists only in back-office documents on the back-office origin.
- The proxy route disables the account API's antiforgery middleware for `/blazor/*`, because the Blazor host validates its
  own forms and reading the form body at the listener would consume the body the forwarder must send.

## Offline shell, push, version gate

None of them runs on a back-office page.

- Service worker: `service-worker-registration.js` registers nothing on a document marked `data-back-office`, and the host
  answers 404 for the worker script and the manifest on the back-office host. The `backOffice` case of `shell-policy`
  asserts that no worker is registered on the back-office origin and that the worker script is not found there. Reason:
  the worker replays the app's offline document, which has no place on the back-office origin.
- Push: no back-office component uses `PushNotificationBrowser`, and without a worker there is no push subscription.
- Version write gate and asset probe: `BackOfficeClient` has its own chain (`BackOfficeApiRegistration`) without
  `StaleClientRequestHandler`, and `StaleAssetProbe` is started only by the app shell. The version window keys on the app
  bootstrap, which the back office does not read. Whether back-office writes need a gate of their own is left to the stage
  that adds the first real back-office write.

## What G6a must deploy

Assumptions until G6a verifies them in Azure:

- On the `back-office` container app: `BACK_OFFICE_BLAZOR_HOST_URL`, the internal URL of the `blazor-host` container app.
  Unset, the route is not mapped and `/blazor/*` falls through to the React back office.
- On the `blazor-host` container app: `BACK_OFFICE_PUBLIC_URL`, the back-office origin
  (`https://<back-office domain>`). Unset, the host serves no back-office page.
- Network: the `back-office` container app reaches the `blazor-host` internal ingress in the same Container Apps
  environment, as the gateway does.
- The data protection key ring shared by the `back-office` and `blazor-host` container apps. The antiforgery relay already
  assumes this (the comment in `HostApplication`); the protected identity adds no new requirement, but a key ring that is
  not shared fails every back-office page with a redirect to the login.
- No new container app and no new authentication configuration: the back-office Easy Auth configuration already covers
  every path of its host.

Local development needs nothing beyond the AppHost, which sets both variables.
