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
  host's root. The dashboard, the back office's home, is `/blazor/back-office`; the spike's placeholder page moved to
  `/blazor/back-office/identity` when the dashboard took its path (EP-204).
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
- On the `blazor-host` container app: `BACK_OFFICE_SUBSCRIPTION_ENABLED`, the Stripe-derived expression the account API
  and the `back-office` container app get as `PUBLIC_SUBSCRIPTION_ENABLED`, so the Blazor back office shows the billing
  parts exactly when the React back office does (added to `main-cluster.bicep` by EP-204; locally the AppHost sets it to
  `"true"`, the account API's value). Unset or anything but `"true"`, the billing parts are hidden.
- No new container app and no new authentication configuration: the back-office Easy Auth configuration already covers
  every path of its host.

Local development needs nothing beyond the AppHost, which sets both variables.

## As built by G6a

Written by G6a (EP-216), 2026-09-28. Nothing was sent to Azure; every Azure statement below remains an assumption until
G6b's checks confirm it.

- `cloud-infrastructure/cluster/main-cluster.bicep` gives the `back-office` container app `BACK_OFFICE_BLAZOR_HOST_URL`
  (the variable `blazorHostInternalUrl`, `https://blazor-host.internal.<environment domain>`, which the gateway's
  `BLAZOR_HOST_URL` now shares) and the `blazor-host` container app `BACK_OFFICE_PUBLIC_URL` (`https://<backOfficeHost>`,
  the same host the account API gets as `BackOffice__Host`, so the route's `RequireHost` and `BackOfficeOrigin` compare
  the same name). `account-api` and `account-workers` do not get `BACK_OFFICE_BLAZOR_HOST_URL`.
- `BACK_OFFICE_SUBSCRIPTION_ENABLED` on `blazor-host` was already the Stripe-derived expression the `back-office`
  container app gets as `PUBLIC_SUBSCRIPTION_ENABLED` (EP-204); unchanged.
- Network, key ring and authentication: no change. Both apps are in the same environment, the `blazor-host` ingress stays
  internal, every container app has `runtime.dotnet.autoConfigureDataProtection: true`
  (`cloud-infrastructure/modules/container-app.bicep`), and the back-office Easy Auth configuration
  (`container-app-auth-config.bicep`) already covers `/blazor/*`. No app registration change, no new ingress, no gateway
  route change.
- `cloud-infrastructure/cluster/deploy-container.sh` needed no code change. Its usage text now names the account images
  G6b must deploy (the proxy lives in the account API image) and the BLAZOR106 clean-up after a Blazor deploy.
- Local proof, 2026-09-28: the image built from the Dockerfile (from a scratch build context holding the publish, so
  nothing was copied into `blazor/Blazor.Host/publish`) ran under `ASPNETCORE_ENVIRONMENT=Production` on the Blazor host
  port, behind the account API's back-office listener and the gateway of `start-stack --without-blazor-host`. It
  answered its health endpoints, served back-office pages to the mocked admin and user only through the listener, and
  answered the same forged headers on the app host and on its own port with 404 or the login redirect.

## Deployment procedure for G6b

Staging only. Production waits for its back-office app registration (EP-192). `<bo>` is the back-office host,
`<app>` the app host, `<rg>` the cluster resource group (`<prefix>-stage-<acronym>`).

1. Record the state before: the image of `account-api`, `back-office`, `account-workers` and `blazor-host`
   (`az containerapp show -n <app> -g <rg> --query "properties.template.containers[0].image"`), and the host names
   bound to `back-office` (`--query "properties.configuration.ingress.[fqdn, customDomains[].name]"`). If the running
   account image predates the newest file under `application/account/Core/Database/Migrations/`, apply the migrations
   first.
2. Account images, one tag for all three apps, as the usage text of `deploy-container.sh` says: `account-workers`,
   `account-api`, then `back-office` (`--container-app back-office`).
3. Blazor image: `blazor-publish --version <tag>`, copy the publish to `blazor/Blazor.Host/publish`,
   `deploy-container.sh <prefix> stage blazor-host <tag> --context blazor --dockerfile ./Blazor.Host/Dockerfile
   --cluster-location-acronym <acronym>`, then delete `blazor/Blazor.Host/publish` (EP-194).
4. The two settings: `deploy-cluster.sh` without `--apply` first, whose what-if must show the two new environment
   variables and no other change beyond the new revisions, then with `--apply`. Without the cluster parameters at hand,
   `az containerapp update -n back-office -g <rg> --set-env-vars BACK_OFFICE_BLAZOR_HOST_URL=https://blazor-host.internal.<environment domain>`
   and `az containerapp update -n blazor-host -g <rg> --set-env-vars BACK_OFFICE_PUBLIC_URL=https://<bo>` set the same
   values the Bicep holds, so the next cluster deploy changes nothing.
5. The checks below, in order; stop at the first failure and keep the React back office as it is.

| # | Check | Request | Expected answer |
| --- | --- | --- | --- |
| 1 | Host (G0 item 3) | Compare the host in step 1 with `BACK_OFFICE_PUBLIC_URL` on `blazor-host` and `BackOffice__Host` on `back-office`; then `GET https://<bo>/api/back-office/me` signed in | All three name the same host; 200 with the session's identity. A mismatch answers every back-office page with 404 |
| 2 | Shared key ring (G0 item 1) | Signed in as an admin, open `https://<bo>/blazor/back-office/identity` | 200 with the signed-in name and the admin marker. A loop between `/blazor/back-office` and `/.auth/login/aad`, or "The forwarded back-office identity is not valid" in the `blazor-host` log, means the ring is not shared: stop, the fallback (an explicit shared key store with `SetApplicationName` on both apps) is an owner decision |
| 3 | Principal header overwrite (G0 item 2) | With the admin session, send `X-MS-CLIENT-PRINCIPAL-NAME`, `X-MS-CLIENT-PRINCIPAL-ID` and `X-MS-CLIENT-PRINCIPAL` naming another identity to `GET /api/back-office/me` and `GET /blazor/back-office/identity`; repeat with a non-admin session and the headers claiming the admins group; repeat with no session | The session's own identity and admin verdict on both; the non-admin stays without the admin marker and its write (for example the rollout of a flag) answers 403; no session answers 302 to `/.auth/login/aad`, never 200 |
| 4 | App path refuses | `GET https://<app>/blazor/back-office` with the forged principal headers, with `X-Forwarded-Host: <bo>` added, and with a made-up `X-Back-Office-Identity` | 404 each time |
| 5 | Internal ingress stays internal | `az containerapp show -n blazor-host -g <rg> --query properties.configuration.ingress.external`; `GET https://blazor-host.<environment domain>/` from outside | `false`; no answer from the app |
| 6 | Content security policy | Response headers of `GET https://<bo>/blazor/back-office` | One `Content-Security-Policy`, nonce based, naming `https://<bo>` and not `<app>` |
| 7 | Offline shell, push, version policy | `GET https://<bo>/blazor/service-worker.js` and `/blazor/manifest.webmanifest`; the page's `<html>` element; the browser's list of service workers for `<bo>` | 404 and 404; `data-back-office` present; no worker registered, so no push subscription; the only WebAssembly component is the back-office one, which does not start the app shell's stale-asset probe |
| 8 | Antiforgery write | As an admin, one write from the Blazor back office, then the same write as a non-admin | 2xx, then 403 |
| 9 | Subscription setting | `BACK_OFFICE_SUBSCRIPTION_ENABLED` on `blazor-host` against `PUBLIC_SUBSCRIPTION_ENABLED` on `back-office`; the account tabs in both back offices | Equal values; the billing tabs show in the Blazor back office exactly when they show in the React one |
| 10 | Nothing else moved | `GET https://<bo>/` signed in, and `GET https://<app>/blazor/` | The React back office at the root; the app edition's landing page, 200 |

The first request after idle can take 20 to 46 s while a container app scales from zero; retry before judging a timeout.
