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
  `IHttpForwarder` to `BACK_OFFICE_BLAZOR_HOST_URL`, with a client from YARP's `ForwarderHttpClientFactory`, the factory
  the app gateway's configured routes use for the same address; locally it also accepts the Blazor host's development
  certificate. A request that arrives with `Content-Length: 0`, and for which the forwarder set up no body of its own, is
  forwarded without a body and without content headers; with the empty body YARP would otherwise attach, the internal
  ingress reset a share of the WebAssembly asset streams on staging (see "Asset stream resets" below). (Corrected by
  EP-226 on 2026-09-30: the earlier text credited the factory with carrying that load; the factory did not change the
  failure rate, the empty body was the trigger.) It removes exactly eight inbound headers, the list
  `BackOfficeBlazorProxy.RemovedRequestHeaders` (lines 25 to 35, verified at `ed3301b34`): the three principal headers
  `X-MS-CLIENT-PRINCIPAL-NAME`, `X-MS-CLIENT-PRINCIPAL-ID` and `X-MS-CLIENT-PRINCIPAL`, any inbound
  `X-Back-Office-Identity`, and the four forwarding headers `X-Forwarded-For`, `X-Forwarded-Host`, `X-Forwarded-Proto`
  and `X-Forwarded-Prefix`. It then sets `X-Back-Office-Identity`, `X-Forwarded-Host` (the back-office host the route
  matched), `X-Forwarded-Proto` and `X-Forwarded-For`, and clears `Host`, so the request names the Blazor host's own
  address and the Blazor host learns the back-office host only from `X-Forwarded-Host`. Azure Container Apps routes by
  `Host`: observed on staging on 2026-09-29, a request to the internal Blazor host that still carried the back-office
  host name (YARP's base `HttpTransformer` copies the inbound `Host`) was answered 404 by the environment ("This
  Container App is stopped or does not exist") and never reached the Blazor host. The app gateway's route to the same
  address already sent the destination's host, YARP's default. Every other header passes through to the Blazor host,
  including the browser's cookies (the platform's session cookie among them), the `X-Original-For` and
  `X-Original-Proto` headers the account API's forwarded headers middleware adds, and any other
  `X-MS-CLIENT-PRINCIPAL-*` or `X-MS-TOKEN-*` header the platform may add. Observed on staging on 2026-09-30 with a
  temporary echo target in place of the Blazor host: the forwarded request carried `X-MS-CLIENT-PRINCIPAL-IDP: aad`, no
  `X-MS-TOKEN-*` header (no token store is configured), the platform's `AppServiceAuthSession` cookie, and the
  `X-Original-For` and `X-Original-Proto` headers. That is harmless while the Blazor host reads only
  `X-Back-Office-Identity`. (Wording corrected after the G6a review, EP-202 N-1; the earlier text said "the
  `X-MS-CLIENT-PRINCIPAL*` headers" and "every inbound `X-Forwarded-*` header".)
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
  `BACK_OFFICE_BLAZOR_HOST_URL` under its own host name (forwarded headers middleware takes the back-office host from
  `X-Forwarded-Host`, the only place it travels; assumption until staging observes it: the environment's internal ingress
  keeps that header as the listener set it), `BackOfficeSurface` admits only
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
- Changed by EP-223 (T019, `ed3301b34`) on the owner's decision on the G0 review's N-2. Verified at `ed3301b34`:
  `BackOfficeIdentityHandler.HandleAuthenticateAsync` returns NoResult before it reads any header unless
  `BackOfficeListener.Received` accepts the request
  (`application/shared-kernel/SharedKernel/Authentication/BackOfficeIdentity/BackOfficeListener.cs` lines 28 to 52).
  In Azure (`AZURE_CLIENT_ID` set) it accepts a request only when `BackOffice:IsBackOfficeContainer` is true, which
  `main-cluster.bicep` sets on the `back-office` container app only (line 489). Everywhere else it accepts a
  request only on the port in `BACK_OFFICE_KESTREL_PORT`, and nothing when that is unset. Principal headers sent to the
  `account-api` container app or the main listener therefore never become a back-office identity. What stays open is
  Development only: without a mock session the back-office listener still passes forged principal headers through
  (the G0 review's N-3, EP-223 N-1).

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
  Indirect staging evidence that the ring is already shared across container apps (cited after the G6a review,
  EP-202 N-2 and EP-216 N-4; verified at `ed3301b34` by reading): the Blazor edition's static login form
  (`blazor/Blazor.Host/Components/Pages/Public/Login.razor` line 25) posts to the Blazor host, whose
  `HostAccountApiHandler` relays the host-issued antiforgery pair, the `x-xsrf-token` header and the
  `__Host-xsrf-token` cookie (`blazor/Blazor.Host/Account/HostAccountApiHandler.cs` lines 56 to 65), to the account API
  at `ACCOUNT_API_URL`. The account API's `AntiforgeryMiddleware` validates that pair with its own data protection
  provider on `/api/account/authentication/email/*`, which does not disable antiforgery, and no file under
  `cloud-infrastructure/`, `.github/` or `application/AppHost/` sets `BypassAntiforgeryValidation`.
  `docs/BLAZOR.md` line 341 records email signup and login through the Blazor edition as proven on staging on
  2026-09-26. The `blazor-host` and `account-api` container apps can pass that only if each unprotects the other's
  payloads, so with a shared ring and matching application discriminators (assumption: the discriminator follows the
  content root, and both images use `WORKDIR /app`). `back-office` runs the account API image in the same environment.
  Staging check 2 stays the proof for `back-office`.
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

## Asset stream resets

Found by G6b (EP-217) on 2026-09-29 and fixed by EP-226 in `e6ddb42d9`, deployed to staging as account images
`2026.09.30.1039` on 2026-09-30.

- Symptom, observed on staging on 2026-09-29: 2 to 15 of 195 parallel `/blazor/_framework/*` downloads through the
  proxy answered 502 with an empty body or were cut off after a 200 (`ERR_HTTP2_PROTOCOL_ERROR` in Chrome), and the
  `back-office` log had `Yarp.ReverseProxy.Forwarder.HttpForwarder[48]` with `HttpProtocolException: The HTTP/2 server
  reset the stream. HTTP/2 error code 'INTERNAL_ERROR'`. One failed file stopped the WebAssembly runtime, so the charts
  and lists never loaded.
- Cause, verified on staging on 2026-09-30: the proxied GETs reach the account API with `Content-Length: 0` (seen in the
  forwarded request through a temporary echo target; browser GETs carry no such header, so the platform authentication
  presumably adds it, assumption). YARP's base `HttpTransformer` keeps that header by attaching an empty body
  (`EmptyHttpContent`, shown by `BackOfficeBlazorProxyTests` before the fix). A plain GET with only
  `content-length: 0` added, sent from outside through the app gateway (YARP, the same client factory and the same
  internal address), reproduced the symptom: 10 of 198 asset requests reset, with the same `INTERNAL_ERROR` in the
  gateway's log; without that header, 0 of 198. Where the reset happens is assumed to be the internal ingress: curl
  straight at `blazor-host.internal` from inside the environment with the same header, or with a request stream that
  ends late, never reproduced it (1,188 requests), and `blazor-host` completed every response.
- Ruled out on 2026-09-29 and 2026-09-30, each by observation on staging: `blazor-host` scaling (resets with one replica
  up for minutes); the size and set of the forwarded headers (4 KB and 16 KB identity headers, the session cookie and the
  forwarding headers, 0 failures from inside the environment); a slow or stalled reader (a 3 s freeze and a 25 % duty
  cycle, 0 failures); the proxy's CPU (at 1.0 vCPU the proxied assets took 295 ms at the median against 0.7 to 1.9 s at
  0.25 vCPU, and 2 of 195 still failed, one in 291 ms); the client factory (`df9d94a91` left the rate unchanged).
- Fix: `BackOfficeBlazorProxy.BackOfficeIdentityTransformer` clears the request content when the inbound
  `Content-Length` is 0 and the forwarder set up no body, so the request goes out without a body and without content
  headers. A body the forwarder set up is kept, because YARP refuses a transformer that replaces it. Covered by three
  tests in `application/account/Tests/BackOffice/BackOfficeBlazorProxyTests.cs`.
- Result, verified on staging on 2026-09-30: check 11 answered 195 of 195 twice (12:15:03 and 12:15:25 UTC), and the
  `back-office` log from the deploy to 12:20 UTC holds 533 framework assets answered 200 and no `HttpForwarder[48]`.
  The dashboard's chart cards render in a fresh Incognito window. In the window used before the fix, the charts only
  rendered after one reload with the cache disabled; assumption: the browser had kept a broken copy of an immutable,
  fingerprinted asset from a failed load.
- Not settled: whether the app gateway is exposed the same way. Its clients' GETs are assumed to carry no `Content-Length`
  (inferred from the app edition's assets loading in Chrome; not observed), so it was left unchanged; a client that sends `Content-Length: 0` through it would
  meet the same resets (reproduced by curl on 2026-09-30).

## Deployment procedure for G6b

Staging only. Production waits for its back-office app registration (EP-192). `<bo>` is the back-office host,
`<app>` the app host, `<rg>` the cluster resource group (`<prefix>-stage-<acronym>`), `<env>` the Container Apps
environment's default domain. Completed by T021 (EP-225) with the additions the G6a review (EP-220, findings N-1 to N-5
on EP-216) and the G7a review (EP-224, its verdict's item 5 and N-4 on EP-223) asked for. Code references are verified at
`ed3301b34`; every statement about Azure is an assumption until G6b observes it.

1. Record the state before, in a file kept until the deploy is judged, and stop before step 2 if a precondition fails.
   - For each of `account-api`, `back-office`, `account-workers` and `blazor-host`, the image and every environment
     variable: `az containerapp show -n <app> -g <rg> --query "properties.template.containers[0].[image, env]"`. Secrets
     appear as `secretRef` names, not values. Step 6 restores exactly this record.
   - The host names bound to `back-office`: `--query "properties.configuration.ingress.[fqdn, customDomains[].name]"`.
   - Precondition, the back-office container flag (G7a review): `az containerapp show -n back-office -g <rg> --query
     "properties.template.containers[0].env[?name=='BackOffice__IsBackOfficeContainer'].value"` answers `["true"]`, and
     the same query on `account-api` answers `[]`. From step 2 on, the account image trusts the principal headers only
     where that flag is true (`BackOfficeListener.FromConfiguration`, lines 28 to 36), and the flag already selects the
     React back office's fallback (`application/account/Api/Program.cs` line 99). The Bicep sets it on `back-office`
     only (`main-cluster.bicep` line 489). Any other answer: stop; the fix is an owner decision.
   - Precondition, the proxy variable (G6a review N-3): `az containerapp show -n account-api -g <rg> --query
     "properties.template.containers[0].env[?name=='BACK_OFFICE_BLAZOR_HOST_URL']"` answers `[]`.
   - Precondition, the database (G6a review N-5). In Azure nothing migrates at start: the workers apply migrations only
     locally (`application/account/Workers/Program.cs` lines 32 to 37). Compare the newest migration applied on staging
     with the newest file under `application/account/Core/Database/Migrations/` (`20260921215015_AddPushSubscriptionDeviceSlot.cs`
     at `ed3301b34`), the way `.github/workflows/_migrate-database.yml` reaches the server: from
     `cloud-infrastructure/cluster`, with `CLUSTER_RESOURCE_GROUP_NAME=<rg>`, `POSTGRES_SERVER_NAME=<rg>` and
     `DATABASE_NAME=account`, run `bash ./firewall.sh open`; read the server's Entra administrator with
     `az postgres flexible-server microsoft-entra-admin list --resource-group <rg> --server-name <rg> --query "[0].principalName" --output tsv`
     and a token with `az account get-access-token --resource-type oss-rdbms --query accessToken --output tsv`; then, with
     the token as `PGPASSWORD`, `psql "host=<rg>.postgres.database.azure.com dbname=account user='<administrator>' sslmode=verify-full sslrootcert=system" -c "SELECT * FROM __ef_migrations_history ORDER BY 1 DESC LIMIT 1"`
     (the history table named in `SharedInfrastructureConfiguration.cs` lines 161 and 167); then `bash ./firewall.sh close`.
     If the newest applied migration is older than the newest file, stop: applying it is an owner step of its own, by the
     workflow's method (with the firewall open, `dotnet ef migrations script <last applied> <last pending> --idempotent`
     for `account/Core/Account.csproj` with startup project `account/Api/Account.Api.csproj` and context
     `AccountDbContext`, then `psql -v ON_ERROR_STOP=1 ... -f migration.sql` as the same administrator, then close the
     firewall). The procedure resumes at step 1 once the history shows the newest file. A migration is never reverted:
     every migration file has `Up` only.
2. Account images, one tag for all three apps, as the usage text of `deploy-container.sh` says: `account-workers`,
   `account-api`, then `back-office` (`--container-app back-office`). Afterwards, signed in, `https://<bo>/` still shows
   the React back office and `GET https://<bo>/api/back-office/me` answers 200 with the session's identity; if not, see
   the symptoms below the checks.
3. Blazor image: `blazor-publish --version <tag>`, copy the publish to `blazor/Blazor.Host/publish`,
   `deploy-container.sh <prefix> stage blazor-host <tag> --context blazor --dockerfile ./Blazor.Host/Dockerfile
   --cluster-location-acronym <acronym>`, then delete `blazor/Blazor.Host/publish` (EP-194).
4. The two settings: `deploy-cluster.sh` without `--apply` first, whose what-if must show the two new environment
   variables and no other change beyond the new revisions, then with `--apply`. Without the cluster parameters at hand,
   `az containerapp update -n back-office -g <rg> --set-env-vars BACK_OFFICE_BLAZOR_HOST_URL=https://blazor-host.internal.<env>`
   and `az containerapp update -n blazor-host -g <rg> --set-env-vars BACK_OFFICE_PUBLIC_URL=https://<bo>` set the same
   values the Bicep holds, so the next cluster deploy changes nothing.
5. The checks below, run by one command from the repository (`blazor/tests/staging-acceptance.mjs`, EP-227). Nothing is
   pasted into a browser console. A failed check, or step 2, 3 or 4 not finishing, sends the procedure to step 6.
   - Sign in once per identity: `dotnet run --project developer-cli -- blazor-harness staging-acceptance --tag <tag>
     --resource-group <rg> --subscription "<subscription>" --sign-in admin`, then the same with `--sign-in non-admin`. Each
     opens a Chromium window on a virtual display inside the dev container, shown through noVNC: forward
     `127.0.0.1:6080`, open `/vnc.html` on the forwarded address and enter the one-time password the command prints. The
     admin signs in to the back office with the owner's Entra account and to the app edition as the admin write's target
     user (`--app-user`, default `jasper@etara.dk`); the non-admin with an account outside the admins group. The sessions
     are stored outside the repository (`~/.local/state/platformplatform/blazor-staging`, readable by the owner only) and
     reused until they expire.
   - Run the checks: the same command without `--sign-in`, in a terminal. An expired session stops it before any check
     (exit code 3) and offers the sign-in again; it is never a failed check. It first asserts that each of `account-api`,
     `back-office`, `account-workers` and `blazor-host` runs `<tag>` on every active revision and that the documents both
     hosts serve reference only assets of that tag's image, then runs every check in the table, the authenticated
     surfaces at desktop and phone width, check 8 on a user override of `--flag` (default `compact-view`, which must be
     user-configurable and active for the preferences page to list it; the owner had it activated on staging on
     2026-10-01) with the target's override state restored exactly afterwards (also on
     an interrupt; a pending-restore file beside the sessions makes the next run restore it first), and the non-admin
     checks.
   - Checks 3b and 3c: the command describes the probe job it would create (from `blazor/tests/staging/probe-job.yaml`,
     no ingress, no secret) and asks for its name to be typed. Typed, it creates the job, runs one execution, reads its
     log and deletes the job; Enter records both checks as not run. Every run asks again.
   - The record: every request and answer in `.workspace/blazor-tests/staging-acceptance-<tag>-<browser>-<time>.json`,
     with cookie and token values masked. Exit code 0 when every check passed, 1 when one failed or did not run.
6. Revert (G6a review N-1). It restores every container app to the image and settings recorded in step 1.
   - Unmap the proxy: `az containerapp update -n back-office -g <rg> --remove-env-vars BACK_OFFICE_BLAZOR_HOST_URL`.
     Unset, the route is not mapped (`application/account/Api/BackOfficeBlazorProxy.cs` line 40) and `/blazor/*` on
     `<bo>` falls through to the React back office. Then `az containerapp update -n blazor-host -g <rg> --remove-env-vars BACK_OFFICE_PUBLIC_URL`;
     without the protected identity that variable grants nothing, but the revert restores the record.
   - Restore each image that differs from the record, in the reverse order of steps 2 and 3 (`blazor-host`,
     `back-office`, `account-api`, `account-workers`): `az containerapp update -n <app> -g <rg> --image <recorded image>`.
     Leave out `--revision-suffix`; the recorded version's suffix is already taken by an existing revision (assumption:
     the platform then names the revision itself). The variable removals above can go in the same update, one revision
     per app.
   - Set back any other variable that differs from the record with `--set-env-vars`, or remove it with
     `--remove-env-vars`. After a `deploy-cluster.sh --apply` in step 4, compare all four apps, not only the two above.
   - A migration applied in step 1 stays. The recorded image then runs against the newer schema (assumption: it copes;
     `AddPushSubscriptionDeviceSlot` adds `device_slot` with default 0 and a unique index on `user_id` and `device_slot`,
     so an image older than it may fail to store a second push subscription for one user).
   - Confirm the React back office is back as it was: for each of the four apps the query of step 1 answers exactly the
     record, and its active revision is healthy (`az containerapp revision list -n <app> -g <rg> --query "[?properties.active].[name, properties.healthState]"`);
     signed in, `https://<bo>/` shows the React back office and its account list loads; `GET https://<bo>/blazor/back-office`
     answers the React back office's document, which carries no `data-back-office` attribute (no file under
     `application/account/BackOffice/` sets it); `GET https://<bo>/api/back-office/me` answers 200 with the session's
     identity; `GET https://<app>/blazor/` answers 200 with the app edition's landing page.
   - Record the failure and the revert on EP-217. The Bicep still holds both variables (`main-cluster.bicep` lines 496
     and 776), and `deploy-cluster.sh` deploys the versions it reads from the running apps (line 81), so the next
     `deploy-cluster.sh --apply` puts both variables back. Before that deploy, the Bicep changes or the cause is fixed;
     that is an owner task, not part of this procedure.

| # | Check | Request | Expected answer |
| --- | --- | --- | --- |
| 1 | Host (G0 item 3) | Compare the host in step 1 with `BACK_OFFICE_PUBLIC_URL` on `blazor-host` and `BackOffice__Host` on `back-office`; then `GET https://<bo>/api/back-office/me` signed in | All three name the same host; 200 with the session's identity. A mismatch answers every back-office page with 404 |
| 2 | Shared key ring (G0 item 1) | Signed in as an admin, open `https://<bo>/blazor/back-office/identity` | 200 with the signed-in name and the admin marker. A loop between `/blazor/back-office` and `/.auth/login/aad`, or "The forwarded back-office identity is not valid" in the `blazor-host` log, means the ring is not shared: stop, the fallback (an explicit shared key store with `SetApplicationName` on both apps) is an owner decision. The staging proof of the Blazor edition's email login is indirect evidence that the ring is shared (see "What G6a must deploy"); this check is the proof for `back-office` |
| 3 | Principal header overwrite (G0 item 2, G6a review N-2) | The command, from a page of `https://<bo>/blazor/back-office` signed in as the admin: `fetch("/api/back-office/me", { headers: h }).then(r => r.json())` and `fetch("/blazor/back-office/identity", { headers: h }).then(r => r.text())`, where `h` is `{ "X-MS-CLIENT-PRINCIPAL-NAME": "<other name>", "X-MS-CLIENT-PRINCIPAL-ID": "<other id>", "X-MS-CLIENT-PRINCIPAL": "<base64 payload naming the other identity with the admins group>" }`. The fetch carries the platform's session cookie and stays inside the page's `connect-src`, which names the back-office origin (`blazor/Blazor.Host/Shell/HostShell.cs` line 190). Repeated as the non-admin with the payload claiming the admins group. Repeated with no session, as a plain request without a cookie, once with `Accept: text/html` and once with `Accept: application/json` | With a session: `/me` names the session's identity with its own `isAdmin`; in the page text, `data-testid="back-office-name"` holds the session's name and `data-testid="back-office-admin-marker"` reads `Admin` for the admin and `Not admin` for the non-admin in en-US (`BackOfficeIdentityPage.razor` lines 19 and 21, read as `blazor/Blazor.Tests/Account/HostSecurityTests.BackOffice.cs` lines 43 to 60 read them); the non-admin's write (for example the rollout of a flag) answers 403. No session: 302 to `/.auth/login/aad` or 401, never 200 (assumption: the platform may answer a request that does not look like a browser's with 401, as the account API's own challenge does, `BackOfficeIdentityHandler.HandleChallengeAsync`). The no-session case is the one that says something about the platform: locally the mock overwrites the headers only when a session exists (G0 review N-3) |
| 3b | Forged headers on the internal account API (G7a review, optional) | From a shell inside the environment: `GET https://account-api.internal.<env>/api/back-office/me` with `Host: <bo>`, the forged headers of check 3 and `Accept: application/json` | 401 or 404, never 200. 401 is the account API refusing the headers outside the `back-office` container (T019); 404 means the environment did not route the forged `Host` to `account-api`. Record which |
| 3c | Forged headers on the back-office app from inside (G7a review, optional) | From the same shell: `GET https://<bo>/api/back-office/me`, or the `back-office` app's own FQDN, with the forged headers of check 3 and no session | 302 or 401, never 200. The account API trusts every request the `back-office` container receives, so this rests on the platform authentication sitting in front of in-environment traffic too (assumption, stated nowhere in the repository) |
| 4 | App path refuses | `GET https://<app>/blazor/back-office` with the forged principal headers, with `X-Forwarded-Host: <bo>` added, and with a made-up `X-Back-Office-Identity` | 404 each time |
| 5 | Internal ingress stays internal | `az containerapp show -n blazor-host -g <rg> --query properties.configuration.ingress.external`; `GET https://blazor-host.<env>/` from outside | `false`; no answer from the app |
| 5b | The proxy stays on `back-office` (G6a review N-3; G7a review) | After step 4, the precondition queries of step 1 again: `BACK_OFFICE_BLAZOR_HOST_URL` on `account-api`, and `BackOffice__IsBackOfficeContainer` on `back-office` and `account-api` | `[]`; `["true"]` and `[]`. Were the proxy variable set on `account-api`, the route would exist there; locally, where one process serves both listeners with it set, forged headers under a forged `Host` rendered a back-office page on the main listener before T019 (the G6a review's probe P25b) |
| 6 | Content security policy | Response headers of `GET https://<bo>/blazor/back-office` | One `Content-Security-Policy`, nonce based, naming `https://<bo>` and not `<app>` |
| 7 | Offline shell, push, version policy | `GET https://<bo>/blazor/service-worker.js` and `/blazor/manifest.webmanifest`; the page's `<html>` element; the browser's list of service workers for `<bo>` | 404 and 404; `data-back-office` present; no worker registered, so no push subscription; the only WebAssembly component is the back-office one, which does not start the app shell's stale-asset probe |
| 8 | Antiforgery write | As an admin, one write from the Blazor back office, then the same write as a non-admin | 2xx, then 403 |
| 9 | Subscription setting | `BACK_OFFICE_SUBSCRIPTION_ENABLED` on `blazor-host` against `PUBLIC_SUBSCRIPTION_ENABLED` on `back-office`; the account tabs in both back offices | Equal values; the billing tabs show in the Blazor back office exactly when they show in the React one |
| 10 | Nothing else moved | `GET https://<bo>/` signed in, and `GET https://<app>/blazor/` | The React back office at the root; the app edition's landing page, 200 |
| 11 | Assets under load (added by G6b, 2026-09-29) | The command, from a page of `https://<bo>/blazor/back-office` signed in as the admin: fetch every `/blazor/_framework/*.wasm` file the page loaded three times in parallel with `cache: "no-store"`; then the `back-office` log for `Yarp.ReverseProxy.Forwarder.HttpForwarder[48]` | Every response 200 with a body; no forwarder error logged. A 502 with an empty body, or a body cut off, is the stream reset of "Asset stream resets" above |

Checks 3b and 3c need a shell inside the environment. None of the repository's images has one: all are chiseled .NET
images (the `FROM` lines of all six Dockerfiles, under `application/account/`, `application/main/`,
`application/AppGateway/` and `blazor/Blazor.Host/`; assumption: chiseled images carry no shell), so `az containerapp exec` into an existing app does not give one. The command
runs them from a temporary Container Apps job with curl, created from `blazor/tests/staging/probe-job.yaml` and deleted
after one execution, only when the owner types the job's name for that run (step 5); without that approval it records
3b and 3c as not run.

Symptoms after step 2 and what to look at first:

| Symptom | Likely cause | Action |
| --- | --- | --- |
| A login loop on every back-office page, React and Blazor, or 401 from `/api/back-office/me` with a session (G7a review) | `BackOffice__IsBackOfficeContainer` missing on `back-office`: the account image trusts the principal headers nowhere else | Check the variable before anything else. The remedy is the variable, `true` as the record and the Bicep hold it, not a rollback; if it cannot be set, step 6 |
| A loop between `/blazor/back-office` and `/.auth/login/aad` on Blazor pages only | The key ring is not shared (check 2) | Stop; step 6; the fallback is an owner decision |
| No chart cards and no `/api/back-office/*` call after a Blazor page load, while check 11 answers every asset 200 | A browser cache holding a broken copy of an asset from a failed load (assumption, observed once on 2026-09-30) | Reload once with the cache disabled, or use a fresh window; if that does not help, check 11 |
| 404 on every Blazor back-office page | `BACK_OFFICE_PUBLIC_URL` does not name the host `back-office` receives (check 1) | Step 6, unless the owner corrects the value |

The first request after idle can take 20 to 46 s while a container app scales from zero; retry before judging a timeout.
