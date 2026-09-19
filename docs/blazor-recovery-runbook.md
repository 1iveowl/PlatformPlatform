# Recovery runbook, Blazor edition

What an operator does when a release of the Blazor edition goes wrong, and what the local release rehearsal has
and has not proved. The rule an already downloaded client follows is in
[the version policy](blazor-version-policy.md).

Measured on the trimmed Release publishes of `797869f97` plus the change that added this document, 2026-09-19,
with `blazor-harness release-rehearsal --browser chromium` against the local stack
(`start-stack --without-blazor-host`, two publishes served in turn by `blazor-serve --folder`). The result file is
`.workspace/blazor-tests/release-rehearsal-chromium.json`.

## Owner

The engineer who deployed the release owns every case below until the application is healthy again. There is no
rota; this edition is not yet in production, so "owner" means the person who pressed deploy, and the escalation
path is the repository owner.

## Cache policy, and why recovery works at all

| Response | Policy | Where it is set |
| --- | --- | --- |
| Every component document (public and authenticated) | `no-cache, no-store, must-revalidate` plus `Pragma: no-cache` | `HostShell.ApplyPageHeadersAsync` |
| The bootstrap and every account API response | `no-store` | `GetBootstrapHandler`, the account API |
| An asset whose route carries a content fingerprint | `public, max-age=31536000, immutable` | `app.MapStaticAssets()` |
| An asset whose route carries no fingerprint | revalidated, never `immutable` | `app.MapStaticAssets()` |
| `/brand.css`, whose URL carries its content version | `public, max-age=31536000, immutable` | `HostApplication` |
| The web manifest | `no-cache` | `HostApplication` |

A document is never stored, so every reload lands on the release that is served now, with the user information,
antiforgery token and policy nonce of that request. Only an immutable, fingerprinted asset is kept, and a
deployment gives every changed asset a new route. `HostSecurityTests.Caching` asserts each row of this table, and
the rehearsal re-checks the fingerprinted ones on the Production publish.

## Cases

### A stale or broken service worker

Not applicable yet: this edition ships no service worker. When the offline shell adds one, this case gets the
update, skip-waiting and unregistration steps, and the rehearsal gains the cases listed as unavailable below.
**Signal** until then: none, because nothing is cached that a reload does not replace.

### A forced security update: every client must stop writing now

Deploy a release whose minor version differs from the one clients hold. Every already downloaded client is then
outside the window: it keeps reading, refuses its own writes before they are sent, and shows the reload prompt at
the next attempt. A user who does nothing keeps a read-only tab; a user who reloads gets the new release.
**Signal**: the localized "A new version of the application is available" message on the form or as a toast with
a "Reload page" action. Measured: a client at `0.9.0` against a server at `1.0.0` was refused on a write, and the
message was the localized one.
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
5. A tab opened on the previous publish read the account normally and was refused on a write, because its client
   version was outside the window.
6. The current publish was restored. The waiting tab reloaded through the prompt's action, came up on the
   restored release and wrote successfully.

**Signal** that a rollback is in effect for users: reload prompts on writes, and 404s in the gateway log for
routes of the release that was rolled back.
**Action**: either roll forward quickly or accept that every open tab is read-only until it is reloaded. In Azure
an old revision can keep serving its own assets alongside the new one, which this local rehearsal cannot
reproduce; see below.

## Known defect: an unsaved edit can be discarded without a prompt

Observed at `797869f97` on the trimmed publish, 2026-09-19, and reproduced without any deployment: on a page
reached by **enhanced navigation** rather than a document load, the unsaved-changes guard does not intercept a
navigation away, and the edit is discarded silently. On a freshly loaded document the same edit is guarded, as
case 3 above shows.

Consequence for recovery: when the reload prompt's action is taken on a page reached by enhanced navigation, the
edit on that page is lost without a question. The rehearsal records the value that was discarded in its result
file under `measurements.editDiscardedByTheReloadPrompt` rather than asserting a prompt that does not appear.

This is a defect of the guard, not of the version policy, and it belongs to the task that owns
`UnsavedChangesGuard`. It is not fixed here.

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
- **A locale switch or a lazily loaded satellite assembly** after a deployment. The probe watches the client
  assembly's route, not every asset, so a missing satellite assembly is noticed at the next in-app navigation
  rather than at the moment it is requested.

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
