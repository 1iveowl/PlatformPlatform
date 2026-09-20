# Recovery runbook, Blazor edition

What an operator does when a release of the Blazor edition goes wrong, and what the local release rehearsal has
and has not proved. The rule an already downloaded client follows is in
[the version policy](blazor-version-policy.md).

Measured on the trimmed Release publishes of `7ce4df7fe` plus the change that added the pre-write re-check,
2026-09-20, with `blazor-harness release-rehearsal --browser chromium --label ep61-pass2` against the local stack
(`start-stack --without-blazor-host`, two publishes served in turn by `blazor-serve --folder`). The result file is
`.workspace/blazor-tests/release-rehearsal-chromium-ep61-pass2.json`, 17 of 17 cases with 3 unavailable.

## Owner

The engineer who deployed the release owns every case below until the application is healthy again. There is no
rota; this edition is not yet in production, so "owner" means the person who pressed deploy, and the escalation
path is the repository owner.

## Cache policy, and why recovery works at all

| Response | Policy | Where it is set | What asserts it |
| --- | --- | --- | --- |
| Every component document (public and authenticated) | `no-cache, no-store, must-revalidate` plus `Pragma: no-cache` | `HostShell.ApplyPageHeadersAsync` | `HostSecurityTests.Caching.Document_ShouldBeNeitherStoredNorReused` and `AuthenticatedDocument_ShouldBeNeitherStoredNorReused` |
| The bootstrap | `no-cache, no-store` | `GetBootstrapHandler` | `application/account/Tests/Authentication/GetBootstrapTests.cs` |
| Every other account API response | no cache directive at this commit | nothing sets one | nothing; the offline shell stores no API response, by allowlist (the offline shell task), and whether these responses get `no-store` is a separate decision outside this stage |
| An asset whose route carries a content fingerprint | `public, max-age=31536000, immutable` | `app.MapStaticAssets()` | `HostSecurityTests.Caching.StaticAssets_ShouldBeImmutableOnlyWhereTheRouteCarriesAFingerprint` |
| An asset whose route carries no fingerprint | revalidated, never `immutable` | `app.MapStaticAssets()` | the same case |
| `/brand.css`, whose URL carries its content version | `public, max-age=31536000, immutable` | `HostApplication` | `HostSecurityTests.Caching.BrandStylesheet_ShouldBeImmutableBecauseItsUrlCarriesItsContentVersion` |
| The web manifest | `no-cache` | `HostApplication` | `HostSecurityTests.Caching.Manifest_ShouldBeRevalidatedAndNeverImmutable` |

A document is never stored, so every reload lands on the release that is served now, with the user information,
antiforgery token and policy nonce of that request. Only an immutable, fingerprinted asset is kept, and a
deployment gives every changed asset a new route. The last column names the test that asserts each row, and the
rehearsal re-checks the fingerprinted ones on the Production publish.

## Cases

### A stale or broken service worker

Not applicable yet: this edition ships no service worker. When the offline shell adds one, this case gets the
update, skip-waiting and unregistration steps, and the rehearsal gains the cases listed as unavailable below.
**Signal** until then: none, because nothing is cached that a reload does not replace.

### A forced security update: every client must stop writing now

Deploy a release whose minor version differs from the one clients hold. Every already downloaded client is then
outside the window: it keeps reading, and it learns that it is outside at its next in-app navigation or before
its next write, whichever comes first, because the write gate re-reads the server's version and the asset probe
before it forwards a mutation. The write is refused before it is sent and the surface shows the reload prompt. A
user who does nothing keeps a tab that is read-only from the moment it is used again; a user who reloads gets the
new release.
**Signal**: the localized "A new version of the application is available" message on the form or as a toast with
a "Reload page" action. Measured: a client at `0.9.0` against a server at `1.0.0` was refused on a write, and the
message was the localized one; a tab that stayed on one page across the deployment and then saved was refused on
that write, with its unsaved edit still on the form.
**The version this compares is the account API's own**, which the bootstrap carries, so a release of that API
alone reaches open tabs even though no asset route changed.
**Do not** rely on this to revoke a session or a token. A compromised session is ended through the account API's
session revocation, which is a separate mechanism and takes effect on the next access token refresh.

### The API is unavailable

A failed call is a transport failure, and the surface shows the shared transport message. Nothing retries by
itself, no write is repeated, and the client does not treat an outage as a signed-out user. Reads that were
already rendered stay on screen.
**Signal**: the transport message on the form or as a toast, and the account API's own health endpoint
(`/internal-api/ready`) failing.
**Action**: bring the API back, or roll it back. No client action is needed; a client that has not been reloaded
is still current.

### Rollback to the previous revision

Serving the previous publish is safe for a tab that is already open, and that tab is told to reload before it can
write. Measured in the rehearsal, in this order:

1. The current publish was served. A tab wrote successfully, and the document's 91 asset responses all came from
   that publish, 71 of them runtime files, with none missing and none from another publish.
2. The previous publish was served in its place. The newer publish's fingerprinted client assembly route then
   answered 404: **a rollback does not retain the fingerprinted assets of the release it replaces.** 21 of 644
   published routes differed between the two publishes.
3. The open tab's next in-app navigation was blocked by the unsaved-changes dialog, so the deployment did not
   discard its unsaved edit without asking.
4. After that navigation the tab knew its asset set was gone. Its next write was refused with the reload prompt
   and the edit stayed on the form.
5. A second tab that stayed on its page and never navigated was refused on its first write too, because the write
   gate re-reads both signals before it forwards a mutation, and its unsaved edit stayed on the form.
6. A tab opened on the previous publish read the account normally and was refused on a write, because its client
   version was outside the window.
7. The current publish was restored. The tab that had never navigated took the prompt's reload action: the
   unsaved-changes dialog asked before its edit was discarded. The other tab reloaded, came up on the restored
   release and wrote successfully.

**Signal** that a rollback is in effect for users: reload prompts on writes, and 404s in the gateway log for
routes of the release that was rolled back.
**Action**: either roll forward quickly or accept that every open tab is read-only until it is reloaded. In Azure
an old revision can keep serving its own assets alongside the new one, which this local rehearsal cannot
reproduce; see below.

## A stale tab that navigated once holds an edit its runtime never received

Measured at `7ce4df7fe` on the trimmed publishes, 2026-09-20. After a deployment, a tab that navigates inside the
application gets a document from the publish that is served now while its runtime is still the one it was loaded
with. An edit typed into an interactive form of that merged document never reaches .NET: the unsaved-changes
guard was not armed by it (`measurements.staleTabAfterEnhancedNavigation.guardArmedByTheEdit` is `false`), so the
guard has nothing to guard and the reload prompt's action discards what is on screen without asking. In the same
run, the tab that never navigated was armed by its edit
(`measurements.tabThatNeverNavigated.guardArmedByTheEdit` is `true`) and was asked before the same action.

What this is not: a defect of the guard. The task that measured the guard on this surface recorded the same
enhanced-navigation path on a tab that had not crossed a deployment as asking, on the Development host and on a
trimmed publish alike, so what differs here is the document merged from the other publish. The cause is not
established beyond that.

Consequence for recovery: a user who keeps typing in a tab after a deployment can lose what they typed when they
take the reload prompt. The write itself is still refused before it is sent, and the version policy still makes
the runtime read-only, so nothing reaches the server from a stale client. Nothing is fixed here; the rehearsal
measures it in its result file and the case is listed below as unverified.

## What this rehearsal has not verified

The rehearsal is local, on a browser tab, against two publishes served in turn by the developer CLI. These
deployment behaviours remain unverified:

- **An installed application or any service worker.** There is none yet. The offline shell task owns an installed
  application kept open across a deployment and an interrupted update of a cached asset set, and the device pass
  owns the shared-device cases. The rehearsal lists all three as unavailable in its result file.
- **A cold anonymous visit with a service worker enabled.** The public-page budget is measured without one.
- **A multi-revision rollout.** Azure Container Apps can serve two revisions at once, so a client can be served
  assets by one revision and API responses by another, and an old revision can keep serving the asset set of the
  release it belongs to. Locally only one publish is served at a time, so the rehearsal always sees the harsher
  case, where the old asset set is gone at once.
- **Traffic splitting and session affinity** during a rollout, and what a client does when its asset set and its
  API answer come from different revisions.
- **Firefox and WebKit.** The rehearsal was run in Chromium. Nothing in the mechanism depends on a Chromium
  feature, so the other two browsers are expected to behave the same; that is an assumption until the run is
  repeated with `--browser all`.
- **A locale switch, a lazily loaded satellite assembly, or a JavaScript module imported for the first time**
  after a deployment. The probe watches the client assembly's route, not every asset, so a missing satellite
  assembly is noticed at the next in-app navigation or before the next write rather than at the moment it is
  requested. The import map is fixed when the document loads, so a component that imports its module for the
  first time in a stale tab asks for the route of the publish that is gone.
- **A server version change while a tab is open.** The version the window compares is the account API's, and the
  rehearsal redeploys only the Blazor host, so the window itself has been exercised only on a client that was
  loaded from the other publish. That the gate re-reads the bootstrap before a write is covered by unit tests
  (`WebAssemblyAccountApiTests`), not by a browser run.
- **A forward deployment with the older release's tab kept open.** The rehearsal runs current, previous, current,
  and the older release is only ever opened in a fresh tab.
- **The merged document of a stale tab.** `assertAtomicAssetSet` runs only on fresh loads in fresh contexts while
  one publish is served, which is where mixing cannot happen. Enhanced navigation in a stale tab merges the new
  publish's document into the old runtime by design, and what protects that state is the probe firing on that
  very navigation, which makes the runtime read-only until it is reloaded. What that state does to an edit typed
  afterwards is the section above.
- **An edit typed in a stale tab after an in-app navigation.** Measured to be lost when the reload prompt is
  taken, as the section above records; no case asserts a prompt there, because the runtime never learns of the
  edit.

## How to run the rehearsal

```
aspire-stop
start-stack --without-blazor-host
blazor-publish --folder a --version 0.9.0     # the previous release, outside the window
blazor-publish --folder b                     # the current release, matching the account API's version
blazor-harness release-rehearsal --browser chromium
```

The script serves each publish in turn through `blazor-serve --folder`, so no other `blazor-serve` may be running
and the stack must be started without its `blazor-host` resource. It refuses to start when the Blazor host port is
already in use, because a foreign host would answer every request and the publish under test would not be the one
measured. Afterwards, stop the stack and start the everyday one again with `aspire-restart`.
