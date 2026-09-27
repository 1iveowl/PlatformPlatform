# The Blazor edition of PlatformPlatform

This is the interim as-built description of the Blazor edition: what exists, how it is built, tested, released and recovered, and what is not there yet. It describes the code at commit `9d39b3c61`, which is the commit deployed to staging.

**How to read the evidence.** Unless a sentence names another commit or a date, every statement about the code was verified at `9d39b3c61` on 2026-09-26. A measurement names the commit it was taken at. A statement about a running environment names the date it was observed. Work that has not been done is written in the future tense.

## Contents

1. [What the edition is and is not](#what-the-edition-is-and-is-not)
2. [Build root and versions](#build-root-and-versions)
3. [Render modes: the public and the authenticated surface](#render-modes-the-public-and-the-authenticated-surface)
4. [Security](#security)
5. [Localization](#localization)
6. [Lists and forms](#lists-and-forms)
7. [Offline shell, push notifications and the version policy](#offline-shell-push-notifications-and-the-version-policy)
8. [Tests](#tests)
9. [Developer CLI commands](#developer-cli-commands)
10. [Deployment](#deployment)
11. [Release and recovery](#release-and-recovery)
12. [Known gaps and what comes next](#known-gaps-and-what-comes-next)
13. [Why a Blazor edition](#why-a-blazor-edition)

## What the edition is and is not

The Blazor edition is a second frontend for the same backend. It is one ASP.NET Core host, `blazor/Blazor.Host`, serving static server-rendered public pages and WebAssembly components from `blazor/Blazor.Client`, under the path base `/blazor/`. The account API, the database, the gateway and the external login providers are shared with the React edition.

**Surfaces built**, by route under `/blazor` (`blazor/Blazor.Host/Components/Pages/`):

| Surface | Routes |
| --- | --- |
| Public, static | `/`, `/login`, `/login/verify`, `/signup`, `/signup/verify`, `/legal`, `/legal/terms`, `/legal/privacy`, `/legal/dpa` |
| Authenticated, interactive | `/app`, `/app/details`, `/welcome`, `/account/users`, `/account/users/recycle-bin`, `/account/settings`, `/user/profile`, `/user/preferences`, `/user/sessions` |
| Offline shell | `/app/offline` |
| Development only | `/development/*` fixtures used by the tests, refused outside Development (`DevelopmentOnlyPages`) |

Authentication covers email one-time password, Google, Microsoft Entra ID and MitID login, and MitID identity verification from the profile. The authenticated surfaces cover the app shell, users administration with the recycle bin, the profile with avatar upload, preferences, sessions, tenant settings and feature flags, and the phone layout. These surfaces were reviewed as complete at `02814f8db` on 2026-09-18.

Four further pieces serve both editions or the Blazor edition only:

* **Emails.** The five transactional emails (`StartLogin`, `StartSignup`, `ResendEmailLogin`, `UnknownUser`, `InviteUser`) are Razor components in `application/account/Emails/`, rendered by `HtmlRenderer` in `application/shared-kernel/SharedKernel/Emails/RazorEmailRenderer.cs`. Because the backend is shared, the React edition sends these emails too (`797869f97`).
* **Version policy and recovery runbook.** See [Offline shell, push notifications and the version policy](#offline-shell-push-notifications-and-the-version-policy) and [Release and recovery](#release-and-recovery).
* **Offline shell.** A service worker that shows an anonymous "You are offline" document for authenticated routes.
* **Push notifications.** Subscriptions in the account API and a test notification a user sends to their own devices.

**What is not there:**

* **Billing.** No subscription, checkout or invoice surface. The shell has no Billing or Overview link, deliberately. Billing is the next planned surface.
* **The back office.** The React back office remains the operator tool until a Blazor back office is built.
* **Content-carrying notifications.** The only notification is the self-sent test. Nothing sends notifications from the system.
* **A way to revoke the current session.** Neither edition has one.

**React is still served.** The gateway routes `/blazor/{**catch-all}` to the `blazor-host` cluster and everything else to the React edition as before (`application/AppGateway/appsettings.json` lines 269 to 280). The React Email sources in `application/account/WebApp/emails/` and `application/shared-webapp/emails/` are still tracked, and `build --emails` still exists, although the backend no longer uses them. Removing them is planned for when the React edition is retired.

## Build root and versions

`blazor/` is a separate build root with its own SDK. `application/` stays on the SDK its `global.json` pins.

| Item | Value | Where |
| --- | --- | --- |
| SDK | `11.0.100-rc.1.26425.128`, `allowPrerelease: true` | `blazor/global.json` |
| Target framework | `net11.0` for Host, Client and Tests | each `.csproj`, line 4 |
| WebAssembly, WebAssembly.Server, QuickGrid, JwtBearer, Localization | `11.0.0-rc.1.26425.128` | `blazor/Directory.Packages.props` lines 13 to 17 |
| FluentUI (`Microsoft.FluentUI.AspNetCore.Components`) | `5.0.0-preview.26254.1`, a nightly from the feed in `blazor/nuget.config` | `blazor/Directory.Packages.props` line 26 |
| Markdig (legal pages, host only) | `1.3.2` | line 12 |
| xunit, FluentAssertions, Test SDK | `2.9.3`, `7.2.2`, `18.6.0` | lines 28, 9, 27 |

The FluentUI pin must not move. The comment at `blazor/Directory.Packages.props` lines 18 to 25 records that the next preview is broken on RC1. The package upgrade command must exclude it (rule `.claude/rules/blazor/component-library.md`).

The solution is `blazor/Blazor.slnx` with three projects. Project references:

* `Blazor.Client` references `Account.Contracts`, `Account.Client` and `SharedKernel.Localization` from `application/`.
* `Blazor.Host` references `Blazor.Client` and `SharedKernel.Security` only, so EF Core, Npgsql and MediatR stay out of the host.
* `Blazor.Tests` references `Blazor.Host`.

Contracts and typed clients are shared C# code, not generated mirrors.

The plan to put the whole tree on one SDK when .NET 11 reaches general availability is in [blazor-tree-unification.md](blazor-tree-unification.md). It has not been executed.

In the local stack the AppHost adds the host as the resource `blazor-host` (`application/AppHost/Program.cs` lines 198 to 214), unless `APPHOST_EXCLUDE_BLAZOR_HOST=true`. The gateway reaches it on its own port and serves it on the base port (default 9000, `PortAllocation.AppGateway`), at `https://app.dev.localhost:9000/blazor/`.

## Render modes: the public and the authenticated surface

The binding rule is `.claude/rules/blazor/render-modes.md`. Its reasoning is in [blazor-render-mode-rule.md](blazor-render-mode-rule.md).

* **Public surface, static.** Landing, login, signup, the verification pages and the legal pages are static server-rendered Razor with no render mode. They start no WebAssembly download. Their forms are `EditForm`s that post to the host, which calls the account API server to server through the typed client. The host registers only interactive WebAssembly components (`HostApplication.cs` lines 44 and 187); there is no Server or Auto mode.
* **Authenticated surface, interactive.** Each page is a server-rendered page marked `[Authorize]` and `[InteractiveSurface]`. It hosts one `Blazor.Client` component with `@rendermode="InteractiveWebAssembly"`. A component whose content is data behind login prerenders its frame and loads the data in the browser, as `DataList` does. No render mode is set on `Routes`.
* **Crossing the boundary** is always a full document navigation. The verification forms that sign in are not enhanced, links between the surfaces carry `data-enhance-nav="false"`, and logout, tenant switch and a lost session leave through `AuthenticationNavigator`.
* **Legal documents.** The host renders Markdown with Markdig from the React edition's three files, behind an allowlist. Anything else answers 404.

## Security

The binding rule for markup and scripts is `.claude/rules/blazor/content-security-policy.md`.

**Content security policy.** `HostShell.BuildContentSecurityPolicy` (`blazor/Blazor.Host/Shell/HostShell.cs` lines 167 to 186) builds a nonce-only policy per request:

* `script-src` has the trusted hosts, `'nonce-…'`, `'strict-dynamic'` and `'wasm-unsafe-eval'`. There is no `unsafe-eval` and no `unsafe-inline`.
* `style-src` is nonce-based.
* `frame-src`, `object-src` and `base-uri` are `'none'`.
* `worker-src` is `'self'`.
* There is deliberately no `form-action`.

Every component document is sent with the following (`ApplyPageHeadersAsync`):

* `Cache-Control: no-cache, no-store, must-revalidate`, except the offline shell document (`/blazor/app/offline`), which is sent `no-cache, must-revalidate` with no nonce and no cookie so the service worker may store it (`HostShell.cs` lines 121 to 145)
* `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, and a referrer policy and permissions policy

Observed on staging on 2026-09-26: `/blazor/` answered with this policy and `'wasm-unsafe-eval'`.

**Bootstrap contract.** The client does not read identity or configuration from the document. It reads the account API's `GET /api/account/bootstrap/`, which is anonymous and answers `Cache-Control: no-store` (`GetBootstrap.cs` line 27). During prerender the host uses `HostBootstrapSource` instead. The server version the version policy compares comes only from the `APPLICATION_VERSION` entry of a bootstrap the client accepted, never from a header, the query or the document.

**Authentication.** The gateway turns the session cookies into a bearer token before a request reaches the host. The host validates it with the platform's token signing service (`blazor/Blazor.Host/Account/HostAuthentication.cs`), so it trusts exactly the tokens the account API trusts.

**Antiforgery.** Configured in `HostApplication.cs` lines 87 to 96:

* The cookie is `__Host-xsrf-token`: HttpOnly, Secure, SameSite Strict, path `/`.
* The header is `x-xsrf-token`.
* Static forms post the token to the host, and `HostAccountApiHandler` forwards it to the account API.

Observed on staging on 2026-09-25: the cookie carries `secure; samesite=strict; httponly`.

**Uploads.** The host has no upload endpoint. The avatar is read in the browser with a size limit that mirrors the server validator (`Blazor.Client/Profile/AvatarFileRules.cs`, `Components/Images/ImageFileRules.cs`). It is sent as multipart to the account API, which validates it again (`application/account/Client/AccountApiTransport.cs` line 49).

**External login binding.** A provider button on the static login or signup page is a GET form to the account API's start endpoint (`Components/Pages/Public/ExternalLoginStart.cs`). The form carries:

* the edition, so the callback's success and failure destinations stay under `/blazor/`
* the page's culture for a signup
* a return path sanitised by the same rule the account API applies
* for a login, the tenant a tenant switch remembered

A button is shown when the deployment's feature flag for that provider is on, but hiding a button is presentation only: the account API decides whether a flow may start. MitID is offered for login only; an account can never be created with MitID. MitID verification completion is bound to the user who started it, not to the session or tenant. That narrowing was accepted deliberately, and it will be re-tested once the React edition is retired. A refused external login that arrives without a flow cookie lands on the React error page.

**Forwarded headers.** `HostApplication.cs` lines 197 to 211:

* It trusts `X-Forwarded-For`, `-Proto` and `-Host` with a forward limit of 1.
* It accepts only the `PUBLIC_URL` host.
* It trusts loopback and `100.64.0.0/10`, the Container Apps proxy.

Observed on staging on 2026-09-25: every redirect is relative, and a forged `X-Forwarded-Host` changes nothing.

**Container.** The image runs as the non-root user `app` (uid 1654). The health routes carry no policy, frame or cookie headers and report only that the process is responsive (`aa9c078e0`).

**Push notifications.** See [Offline shell, push notifications and the version policy](#offline-shell-push-notifications-and-the-version-policy) for the host allowlist, the subscription cap and the sender's redirect policy.

## Localization

The binding rule is `.claude/rules/blazor/localization.md`.

* **Cultures and strings.** Two cultures, `en-US` (default) and `da-DK`, are declared once in `application/shared-kernel/SharedKernel.Localization/SupportedCultures.cs` and shared with the backend. Strings are `.resx` pairs under `SharedKernel.Localization/Resources/`: `Common`, `Authentication`, `Users`, `Account`, `Legal`, `Email` and `FluentComponent`. Components use the generated classes, for example `@CommonStrings.Key`.
* **How the culture is chosen.** The host chooses it in `HostShell.GetLocale`, in this order: the user's locale claim, the `preferred-locale` cookie, `Accept-Language`, then the default. The same culture carries from prerender into the browser.
* **Emails.** Emails are localized from `EmailStrings`. A signup resend follows the request's culture, and an invitation follows the inviter's.
* **API error messages** are shown in English in every culture. That was decided on 2026-09-14 and is not a defect.

## Lists and forms

The binding rules are `.claude/rules/blazor/lists.md` and `.claude/rules/blazor/forms-and-validation.md`.

**Lists.** Every list page uses `DataList<TItem>` from `blazor/Blazor.Client/Components/Lists/`, with a `<Feature>ListSource` and a host page (`Users/UsersListSource.cs`, `Pages/App/UsersPage.razor`). The list keeps its filters, sort and page offset in the URL and only there, in server pages of 25 rows. On a phone the users list scrolls infinitely, with `pageOffset` in the URL and select-all capped at 100 rows. QuickGrid is never used directly. The component comparison behind this choice is in [blazor-b3-data-grid.md](blazor-b3-data-grid.md), and the capability map in [blazor-capability-map.md](blazor-capability-map.md).

**Static forms** on the public surface follow `Login.razor`:

* `EditForm` with `FormName`, `[SupplyParameterFromForm]` and `DataAnnotationsValidator`
* the model in `PublicForms.cs`
* server errors mapped by `FormErrorMapper` and shown by `FormErrorAlert`

**Interactive forms** show API failures through `ApiFailurePresenter`. Fields carry `aria-invalid` and `aria-describedby` through `FieldAria` and `FieldValidation`.

**Unsaved changes.** `UnsavedChangesGuard` asks before discarding an edit in four cases:

* a navigation started from .NET
* a link click or Back or Forward
* a reload or close (`beforeunload`)
* a dialog close

The account name and the profile fields use `ImmediateInputText`, bound on `input`, so a reload while the field still has focus also asks (`c820bd945`). Choosing Leave discards the edit, and the guard's release ends with the navigation it covered (`b5a22f2d7`). A lost session releases the guard by design.

## Offline shell, push notifications and the version policy

### Offline shell

The service worker template is `blazor/Blazor.Host/wwwroot/service-worker.js`. The host serves it at `/blazor/service-worker.js` (observed on staging on 2026-09-26) with `Cache-Control: no-cache` and `Service-Worker-Allowed: /blazor/`. It is registered only from interactive documents.

**What it answers.** It serves only navigations whose first segment is `app`, `account`, `user` or `welcome`. It tries the network first and falls back to the shell. It never answers a public page or an API call.

**What it caches:**

* The document cache holds exactly `/blazor/app/offline`. That document is anonymous: it has no nonce, no antiforgery token and no cookie, and it is fetched with `credentials: "omit"`.
* The asset cache holds framework, content and fingerprinted assets.
* No API response, no bootstrap and no user data is cached.

**When it clears.** Logout, session end and tenant switch clear the document cache.

**Proven in:**

* Chromium, by the end-to-end specification and the `offline-shell` harness.
* Firefox, by `offline-shell-relaunch`.
* Real Safari 27.0 on macOS 27.0 in a tab, by the device runner: 7 of 7 at `e25c6d209`.
* Staging in Chrome, observed on 2026-09-26.

**Not proven:**

* A manual reading in an everyday Safari profile failed at `21fb82e4f` and was not reproduced. It is recorded in the runbook.
* iOS, an app added to the macOS Dock, and a worker update across a real deployment.

### Push notifications

The endpoints are under `/api/account/users/me/push-subscriptions` and all require authentication (`application/account/Api/Endpoints/PushSubscriptionEndpoints.cs`):

* `GET /`
* `POST /` (upsert)
* `POST /test`
* `DELETE /{id}`

**Ownership and tenancy.** Subscriptions are tenant-scoped and owned by one user. The GET returns id, label and creation time, never the endpoint or keys.

**Rules enforced** (`application/account/Core/Features/PushNotifications/Shared/PushNotificationPolicy.cs`):

* **Push service host allowlist.** The allowed hosts are `fcm.googleapis.com`, `updates.push.services.mozilla.com`, `push.apple.com` and `notify.windows.com`, and their subdomains. Only https on port 443, with no userinfo and at most 2,000 characters, is accepted. The address is checked when it is saved and again when a notification is sent.
* **Subscription cap.** At most 20 subscriptions per user, held atomically by a unique index on `(user_id, device_slot)` (`21fb82e4f`).
* **Test throttle.** One test notification per user per minute, per process.

**The sender** follows no redirects and times out after 10 s. A timeout or any other exception becomes a failed delivery rather than an error. A 404 or 410 from the push service deletes the row.

**Payload.** A notification carries only a title, a body and a rooted path, which the worker re-validates.

**When a user leaves a device.** Before a logout or tenant switch, the device's subscription row is deleted and the browser is unsubscribed (`a6ff87ccd`). A permission denied in the browser is treated as revocation (`4d84b0cad`).

**Switching it on.** Push is on only when the VAPID public key, private key and subject are all configured. `PUBLIC_PUSH_NOTIFICATIONS_ENABLED` follows the same condition in Bicep. Without them, every push endpoint answers 404. Observed on 2026-09-26: push is off on staging, because the `PUSH_*` repository values are not set (a deliberate choice).

### Version policy

[blazor-version-policy.md](blazor-version-policy.md) defines the supported window between a downloaded client and the server.

* **The window.** Equal major and minor is supported. Any other version is unsupported. A version that cannot be parsed is unknown and never blocks a write.
* **The second signal.** `StaleAssetProbe` re-requests the client assembly's own fingerprinted file on every in-app navigation, and a 404 marks the runtime stale.
* **Before a write.** The write gate re-checks both signals before a mutation is sent. A stale client's write stops with a local 412 and the prompt `CommonStrings.ApplicationUpdated`. GET, HEAD, OPTIONS and logout are always sent.

This is why a publish must carry the version it is deployed as (see [Release and recovery](#release-and-recovery)).

## Tests

**xunit.** `blazor/Blazor.Tests` holds 95 test files. They are organised by area:

* host behaviour through `Account/HostFixture.cs`
* client code in `Client/`
* `Public/`, `Shell/` and `Localization/`

At `9d39b3c61`, `test --blazor` ran 1,372 of 1,372, and the application backend ran 1,609. The push notification tests are in `application/account/Tests/PushNotifications/`, and the email tests are with the account tests.

**Harness scripts.** `blazor/tests/*.mjs` holds 24 scripts, run by `blazor-harness <script>`: 23 drive a browser, and `verify-results.mjs` checks their stored results. They cover:

* policy: `shell-policy`, `antiforgery`, `authentication-state`
* the trimmed publish: `trimmed-smoke`, `interactive-load`
* budgets: `public-pages`
* accessibility: `accessibility`, `mobile-surfaces`, `shell-layout`
* forms and lists: `form-errors`, `unsaved-changes`, `data-list`, `side-pane`, `one-time-password`
* surfaces: `account-settings`, `profile-avatar`, `feature-flags`, `localization`
* offline and push: `offline-shell`, `offline-shell-relaunch`, `push-notifications`
* release: `release-rehearsal`, `stale-tab-edit`
* `verify-results`, which checks that every required result is on the current commit and passed

The harness builds its URLs from the local stack's port (`blazor/tests/support/stack.mjs`), so it cannot target a deployed host yet.

**End-to-end specifications.** `blazor/tests/e2e/` holds 20 Playwright specifications, run by `e2e --blazor`. They cover:

* login, signup, Google, Entra and MitID
* localization and localized emails
* users, profile, sessions and tenant switching
* feature flags, navigation, the mobile view and the offline shell

**The accessibility bar** is `.claude/rules/blazor/accessibility.md`: ten criteria, each naming its check and, where one applies, its manual device cells, followed by the instruction to run the bar. `blazor-harness accessibility --browser all` passed 36 of 36 per browser with no serious or critical violation, across 19 surfaces per culture at 1280 px and 390 px (`747c65a37`).

**The device runner** in `blazor/tests/device/` drives real Safari on macOS and writes a verdict with its manual cells. It runs outside the container (`13584c035`). Its iOS Simulator target does not work yet, and Android was not attempted.

**Frozen budgets.** These are defined in `blazor/tests/public-pages.mjs` lines 70 and 80, and the thresholds are not to be raised:

| Pages | Transfer | First contentful paint |
| --- | --- | --- |
| Landing, login, login verify, signup, signup verify, legal index | 125,000 bytes | 300 ms |
| Terms, privacy, DPA | 140,000 bytes | 300 ms |

The budgets are measured on Chromium with a throttled profile: 60 ms latency, 9,000 kbps down and 1,500 kbps up. Each value is the median of 7 samples after 1 warm-up, with 3,000 ms of observation per sample.

At `747c65a37` the medians ran from 114,480 bytes (landing) to 130,370 bytes (DPA), with first contentful paint of 256 to 268 ms. The staging figures below come from a hand measurement that mirrors the script at `9d39b3c61` on 2026-09-25, because the harness cannot target a deployed host:

* 116,961 to 120,118 bytes for the public pages, and 127,448 to 132,918 bytes for the legal documents.
* First contentful paint of 276 to 300 ms. The en-US landing page sits exactly at the 300 ms limit.

**Continuous integration.** `.github/workflows/blazor.yml` has these jobs:

* build and test
* code style: format and lint with `--verify-build`
* the trimmed publish
* the published security regressions: trimmed smoke in both cultures, antiforgery, authentication state, the production shell policy, then the public-page budget and interactive load
* the development shell policy

It does not run the end-to-end specifications or the offline, push, accessibility or release harnesses. Its state is described under [Deployment](#deployment).

## Developer CLI commands

| Command | What it does |
| --- | --- |
| `build --blazor`, `test --blazor`, `format --blazor`, `lint --blazor` | The usual workflow on the Blazor build root. With no target flag, `build`, `format`, `lint` and `test` include it. |
| `check --blazor` | Build, format, lint and test for the Blazor build root. |
| `e2e --blazor` | Runs `blazor/tests/e2e`. It cannot be combined with `--self-contained-system`. |
| `test --spike <name>` | Runs the tests of a spike solution in `blazor/spike/<name>/` (today `c12-public-client`), which resolves its own SDK. |
| `blazor-publish [--folder <name>] [--version <version>]` | Trimmed Release publish of `Blazor.Host` to `.workspace/blazor-publish[-<name>]`. `--version` sets the assembly version the client compares with the server. |
| `blazor-serve [--folder <name>]` | Serves a publish in Production on the Blazor host's port behind the running gateway. |
| `start-stack --without-blazor-host` | Starts the stack without the `blazor-host` resource so `blazor-serve` can take its port. |
| `blazor-harness <script> [--browser chromium\|firefox\|webkit\|all]` | Runs `node blazor/tests/<script>.mjs`. Other options pass through to the script, for example `--check-budget` or `--culture`. |

The command sources are in `developer-cli/Commands/`: `BlazorPublishCommand.cs`, `BlazorServeCommand.cs`, `BlazorHarnessCommand.cs`, and the `--blazor` option in `BuildCommand.cs`, `TestCommand.cs`, `FormatCommand.cs`, `LintCommand.cs`, `End2EndCommand.cs` and `CheckCommand.cs`.

## Deployment

### Artefacts

* **Container image.** Built from `blazor/Blazor.Host/Dockerfile` on `mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1-resolute-chiseled-extra`. It copies a trimmed publish from `blazor/Blazor.Host/publish` and listens on 8080.
* **Health endpoints.** `/internal-api/live` and `/internal-api/ready` are anonymous self-checks. Readiness does not call the account API.
* **Bicep.** `cloud-infrastructure/cluster/main-cluster.bicep` defines the container app `blazor-host` with:
  * its own user-assigned identity, which reads the token signing key, issuer and audience from Key Vault
  * internal ingress
  * 0.25 CPU, 0.5 GiB, and 0 to 3 replicas
  * `ACCOUNT_API_URL`, `PUBLIC_URL`, `CDN_URL=<cdn>/blazor` and the `PUBLIC_*` feature flags
* **Gateway.** The gateway finds the host through `BLAZOR_HOST_URL` (`https://blazor-host.internal.<environment domain>`), falling back to the local port (`ClusterDestinationConfigFilter.cs` line 40).
* **Version.** The host sets no `APPLICATION_VERSION`. Its version is the publish's assembly version, so the publish must use the image tag as its `--version`.
* **Key ring.** The host shares the data protection key ring through `AddCrossServiceDataProtection` (`SharedKernel.Security/Configuration/SecurityDependencyConfiguration.cs` lines 35 to 47). In Azure, each container app sets `runtime.dotnet.autoConfigureDataProtection: true` (`cloud-infrastructure/modules/container-app.bicep` line 181), and the environment manages the keys. Locally, only the application name is set. No code persists keys explicitly. The evidence that the ring holds across replicas is indirect: on staging on 2026-09-26, a signed-in session survived a revision restart that replaced all three replicas, and it survived a new deployment.

### Environments

Observed on 2026-09-26:

| Environment | State |
| --- | --- |
| Local | The Aspire AppHost runs `blazor-host` beside the React edition, at `https://app.dev.localhost:9000/blazor/` with the default base port. |
| Staging | Runs at `https://staging.ppdemo.etara.dk/blazor/`, revision `blazor-host--2026-09-26-1610-fw`, image tag `2026.09.26.1610`, built from `9d39b3c61`. See the staging proofs below. |
| Production | Not deployed. `PRODUCTION_CLUSTER1_ENABLED=false` and no production domain is set, deliberately. The production back office app registration will need the configuration staging needed before the first production deploy. |

The staging proofs cover:

* public pages in both cultures within budget
* email signup and login
* Google and Entra signup and login
* login surviving a revision restart
* rollback and forward
* the offline shell in Chrome
* the React edition loading
* back office sign-in

The following remain unproven on staging:

* the Danish email flows and the two verification pages
* a push notification arriving
* MitID
* a write from a stale tab after a rollback

### How a deployment is made

No workflow builds or deploys the `blazor-host` image. A Blazor deployment is made by hand, following the usage text in `cloud-infrastructure/cluster/deploy-container.sh`:

1. `blazor-publish --version <tag>`, where the tag is the image tag, for example `2026.09.26.1610`.
2. Copy `.workspace/blazor-publish` to `blazor/Blazor.Host/publish`.
3. `cloud-infrastructure/cluster/deploy-container.sh <prefix> stage blazor-host <tag> --context blazor --dockerfile ./Blazor.Host/Dockerfile --cluster-location-acronym eu`. The acronym is required because the script's default is `weu`.
4. Delete `blazor/Blazor.Host/publish`. Otherwise the next `blazor-publish` fails with BLAZOR106.

A production deployment will reuse the verified staging image with `--import-from <staging subscription id>`. It has not been done yet.

The infrastructure itself is deployed by the Cloud Infrastructure workflow on `main`. Every run creates a new revision of all seven apps because the revision suffix is random (`deploy-cluster.sh` line 95). Staging has no environment protection rule, so a dispatch on `main` plans and applies in one go.

### GitHub workflows

The repository's hosted workflows were disabled on 2026-09-19 to save hosted minutes while the edition is built. State read from the repository on 2026-09-26:

| State | Workflows |
| --- | --- |
| Active | `account.yml`, `app-gateway.yml`, `main.yml`, `cloud-infrastructure.yml`, `pull-request-conventions.yml` |
| Disabled | `blazor.yml`, `code-style.yml`, `developer-cli.yml`, and the reusable `_deploy-container.yml`, `_deploy-infrastructure.yml` and `_migrate-database.yml` |

`blazor.yml` stays disabled so that its nightly schedule does not run during the transition. It was enabled only to dispatch run 36256640025 on `main` at `9d39b3c61`, then disabled again. That run failed on 2026-09-26, on two jobs. Build and test, the trimmed publish and the published security regressions passed. The code-style job failed because `format --blazor --all-files` on the runner moves `@layout` above the `@using` directives in six Razor files. The development shell policy failed 3 of its 12 cases, because MitID login and verification are not enabled in the runner's AppHost. `code-style.yml` and `developer-cli.yml` stay disabled until the work needs them; the local `format`, `lint` and `test` commands cover the same checks. They will be re-enabled when the Blazor edition is deployed to production.

## Release and recovery

[blazor-recovery-runbook.md](blazor-recovery-runbook.md) is the operator's runbook. It names the owner (the engineer who deployed, escalating to the repository owner) and covers:

* a stale or broken service worker
* a forced security update
* the API being unavailable
* rollback
* what the local release rehearsal has not verified

**When a client learns of a release.** An open tab learns of a new release at its next in-app navigation or its next write, whichever comes first. It then shows the reload prompt instead of sending the write.

**Caching.**

* Documents are sent `no-store`, except the offline shell document, which is `no-cache, must-revalidate`.
* The bootstrap is `no-store`.
* The manifest and the service worker are `no-cache`.
* Fingerprinted assets are immutable.

Other account API responses carry no cache directive yet. Whether they should is still to be decided.

**Rollback on Azure Container Apps.** In single revision mode, copying a revision puts an earlier template and image back. Staging was rolled back and forward this way on 2026-09-26. Both copies were Healthy with all traffic:

```
az containerapp revision copy --from-revision <earlier revision> --revision-suffix <suffix>-rb
az containerapp revision copy --from-revision <newer revision> --revision-suffix <suffix>-fw
```

After the rollback, the newer client assembly answered 404. That is the runbook's rollback signal, and it sends an open tab to the reload prompt. A signed-in tab kept navigating and reloading. Multi-revision rollout, traffic splitting and session affinity have not been verified.

**Release rehearsal.** `blazor-harness release-rehearsal` serves two publishes in turn on the local stack and checks the policy across them, 20 cases. `stale-tab-edit` checks edits typed on a document from another publish, 14 cases (`b5a22f2d7`).

## Known gaps and what comes next

**Open items:**

* **Cold starts.** Every container app scales to zero, so the first request after idle takes 20 to 46 s. On 2026-09-26 the first request to `/blazor/` took 31.6 s.
* **The publish copy.** The copy `deploy-container.sh` requires breaks the next publish.
* **Harness targets.** The harness scripts cannot target a deployed host.
* **Infrastructure preview.** How a staging infrastructure change is previewed before it is applied is undecided.
* **Production back office.** Its app registration needs configuring before the first production deploy.
* **Apple push subject.** The Apple push service refuses the development push subject.
* **Upstream reports.** `pp deploy` and `github-config` accept wrong OAuth values and hide a failed back office setup, and the git hook sync treats git's error text as a folder path.
* **Worker security findings.** The service worker's asset cache uses a header denylist rather than a path allowlist, and the worker response carries no `nosniff` or policy of its own.
* **Push cleanup edge cases.** A failed delete still clears the device's stored subscription id, and because the row is deleted before a logout is sent, a logout that is then rejected leaves the account without that device's row. Both will be fixed before any notification carries account content.
* **Test coverage.** No workflow runs the end-to-end specifications or the release, offline, push or accessibility harnesses. Firefox and WebKit skip the Playwright offline step. The device cells for iOS, Android, screen readers and standalone launch will be run by hand at each release review.
* **Hosted CI.** The two failures of run 36256640025 described under [GitHub workflows](#github-workflows) are not yet fixed: the Razor directive order that the formatter expects, and the MitID configuration the development shell policy needs on a runner.
* **Release review.** An independent review of this release is pending. It will rerun the security checks against the deployed host and give the release verdict.

**Planned next**, in order. None has started:

1. **Billing.** Billing with Stripe-hosted Checkout and Customer Portal, reusing the backend's webhook handling and reconciliation.
2. **Back office.** The back office in Blazor.
3. **Retiring React.** Removing the React frontends, the npm workspace, the React Email sources and the Node plumbing.
4. **Native authorization.** Native-capable authorization (PKCE, token endpoint, scopes, refresh and revocation), not yet scheduled.

Production deployment and the move to .NET 11 general availability are not yet scheduled.

## Why a Blazor edition

The edition exists for teams that are all C# and count a second language in the stack as a cost:

* **One language, toolchain and debugger.**
* **Shared contracts, not generated mirrors.**
* **Server validation expressed directly in forms.**
* **No Node in the frontend build.** This will be complete once the React edition is retired.
* **A published support lifecycle for the whole stack.**

The costs:

* It starts behind the React edition, which receives upstream improvements first.
* The component ecosystem is thinner.
* It does not look like the React edition.
* The authenticated surface has a larger initial download.
* Localization and the rules corpus had to be rebuilt.

The full case, as it was assessed before anything was built, is kept unchanged in [blazor-assessment.md](blazor-assessment.md). If you have frontend specialists or want the deepest component ecosystem, the React edition remains the stronger choice. The backend architecture is the same in both.
