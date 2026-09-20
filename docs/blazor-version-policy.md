# Client and server version compatibility, Blazor edition

The supported window between an already downloaded WebAssembly client and the server it talks to, what an
unsupported client does, and what this policy does not cover. The operator side of the same subject, including
what to do when a release goes wrong, is [the recovery runbook](blazor-recovery-runbook.md).

Measured on the trimmed Release publishes of `7ce4df7fe` plus the change that added the pre-write re-check,
2026-09-20, by `blazor-harness release-rehearsal --browser chromium --label ep61-pass2`; the result file is
`.workspace/blazor-tests/release-rehearsal-chromium-ep61-pass2.json`. Every number and behaviour below was
observed in that run unless it is marked as an assumption.

## The window

A client is **supported** when its major and minor version equal the server's. Any other client is
**unsupported**. A version that neither side states in a form the policy can parse leaves the window
**unknown**.

- The server's version is the `APPLICATION_VERSION` entry of `RuntimeConfiguration` in the bootstrap response
  the account API returns on the authenticated channel. It is that API's own assembly informational version.
  Observed value in the local stack of `797869f97`, 2026-09-19: `1.0.0+797869f97929c0513c17563e0aaf936b873dfdd1`.
- The client's version is the informational version of the `Blazor.Client` assembly, read through
  `ClientVersionWindow.CurrentClientVersion`. A publish sets it with `blazor-publish --version <version>`.
- Build metadata and a prerelease label name one build of a version, not another contract, so `1.2.3+9fd2c1a`
  and `1.2.3-rc.1` are both in the `1.2` window.
- The version is read only from the bootstrap on the authenticated channel, never from a header, a query value
  or anything injected into the host page.

An unsupported client keeps working for reading and **must not send a mutation**. Its next write is refused
before it leaves the browser, and the surface that attempted it shows a reload prompt: the localized
`CommonStrings.ApplicationUpdated` message with a "Reload page" action. A full document navigation replaces the
client with the one the server now serves, which is what the prompt's action does.

An unknown window never blocks a write. A client that cannot tell whether it is current is not evidence that it
is stale, and refusing every write would take the application down on a build that lost its version metadata.
The release rehearsal is what proves the trimmed publish keeps that metadata: only an unsupported client shows
the prompt, so the case that expects the prompt fails if the client cannot read its own version.

## When a client learns

A client reads both signals again at two moments, and nothing else polls:

- **At an in-app navigation**, where the asset probe re-requests the route it captured.
- **Before it sends a mutation**, where the write gate re-checks both signals: the same asset request, and a fresh
  read of the bootstrap, which is where the server's version comes from. A tab that has been open across a
  deployment and then presses Save has navigated nowhere, so this is the only moment it can learn, and the
  server's version is the account API's own, which changes when only that API is redeployed and no asset route
  does.

The cost is one extra request before a write, or two: writes are rare, and a read is never gated or delayed.
Measured on the publish of `7ce4df7fe`: cold time to interactive 646 ms against 632 ms before the re-check was
added, and the transfer of a cold authenticated start unchanged (4 098 514 bytes against 4 099 243), because
nothing was added to the load.

A re-check that cannot complete never blocks the write: an unreachable server, a document that is gone or a
version neither side states is not evidence that this client is stale, and refusing every write on that would
take the application down during an outage. A runtime already known to be stale asks nothing more.

## The second signal: the asset set is gone

A client also becomes stale without any version changing, because a deployment replaces the content fingerprint
in the route of every changed asset. A document that is already open keeps asking for the routes it was built
with, and those answer 404 once the publish they belong to is no longer served.

`StaleAssetProbe` captures one fingerprinted asset route of the document that started the runtime, preferring
this client's own assembly because its route changes whenever the application's code does. On every in-app
navigation, which is where a tab that has been open across a deployment is used again, it re-requests that route
with `cache: "no-store"`, because a fingerprinted asset is cached for a year and would otherwise answer from the
browser's cache long after the server stopped serving it. A 404 makes this runtime stale from then on, with the
same consequence as being outside the version window.

Measured in the rehearsal: publish B was served, a tab was opened, publish A was served in its place, and the
tab's own client assembly route then answered 404. Its next write was refused with the reload prompt although
its version matched the server's. A second tab that stayed on its page and never navigated was refused the same
way on its first write, because the gate ran that request itself before forwarding anything, and its unsaved
edit stayed on the form.

The shape of a fingerprint is read from the pipeline, not guessed: every route the static web asset pipeline
fingerprints carries exactly ten lowercase base36 characters, digits optional. Measured on the endpoint manifest
of this build: 527 fingerprinted routes, 271 distinct fingerprints, 17 of them letters only, and no route
without a fingerprint carries a segment of that shape. `FingerprintedAssetTests` holds the predicate against
that manifest route by route, so a release whose client assembly draws a letters-only fingerprint is watched
like any other.

## Where the decision lives

| Concern | Where |
| --- | --- |
| The window and its parsing | `blazor/Blazor.Client/Bootstrap/ClientVersionWindow.cs` |
| What this runtime knows about itself | `blazor/Blazor.Client/Bootstrap/ClientVersionState.cs` |
| Whether a route carries a fingerprint, and which one to watch | `blazor/Blazor.Client/Bootstrap/FingerprintedAsset.cs` |
| Watching that route | `blazor/Blazor.Client/Bootstrap/StaleAssetProbe.cs` and `wwwroot/js/stale-assets.js` |
| Reading both signals again before a write | `blazor/Blazor.Client/Bootstrap/StaleClientRecheck.cs` |
| Refusing the mutation | `blazor/Blazor.Client/Bootstrap/StaleClientRequestHandler.cs` |
| Turning the refusal into the prompt | `ApiFailureClassifier` (`ApiFailureKind.Version`), `ApiFailurePresenter`, `FormErrorMapper` |

The write gate is the outermost handler of the client's account API chain, so a refused call computes no
antiforgery token and sends nothing. It answers the call locally with `412 Precondition Failed` and a title the
classifier matches, which is never shown to anyone. Two exceptions pass through: a read, because an old client
stays usable for reading, and logout, because a stale client must always be able to end its session.

The re-check the gate runs before it forwards a mutation reads the bootstrap through `IBootstrapSource` rather
than through `SessionState`, whose refresh also republishes the identity and the feature flags to every surface
that shows them; the re-check changes nothing a surface can see except the one decision it exists for. That read
is a GET, so it passes the gate it is called from and cannot recurse.

## What the policy does not do

- It does not stop a stale client at the server. The gate is a client-side rule about what this runtime is
  willing to send; the account API keeps accepting whatever its contracts accept. A contract change that an old
  client cannot survive therefore needs a minor version bump, which puts every old client outside the window.
- It does not cover a service worker or an installed application. The offline shell is built after this package,
  and the cases that need one are listed as unavailable in the rehearsal's result file.
- It does not poll. A client learns at its next in-app navigation or before its next write, whichever comes
  first. A tab that sits untouched learns nothing, and nothing it does not send can be refused.

## Contract compatibility

The C# contracts in `application/account/Contracts` are shared by the server and the client, so source
compatibility is checked when they are built together. That says nothing about an older client that is already
downloaded, which is what this policy and the rehearsal are for. The rule for a change that an old client cannot
survive, in either direction, is a minor version bump:

- a required field added to a request, or a field removed from a response the client reads
- an enum value the client maps by name, when the client would fail on an unknown value
- an error payload shape the client branches on, including the field keys of a validation problem
- a feature flag whose absence the client treats as enabled

An additive change that an old client ignores needs no bump. When in doubt, bump: the cost is one reload prompt
before the next write, and the cost of being wrong is a write that fails in a way the user cannot act on.
