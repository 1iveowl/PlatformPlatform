# Blazor frontend: revised plan and run plan

Status: owner decisions recorded 2026-09-13 on [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) to [EP-53](https://linear.app/etarapartners/issue/EP-53/decide-whether-the-blazor-rules-and-skills-corpus-is-funded); the Linear record in section 12 is applied. Stage B was reviewed on 2026-09-13 (B4: go for stage C with 14 corrections) and session B5 wrote the delivery plan for stages C to H and N the same day: section 5 fixes every stage's sessions and chains, and sections 6 to 11 carry the stage B inputs, the flow matrix, the calibration and critical path, the upstream rehearsal record, operator recovery and the hop schedule. This document is the one place that defines the stages, the sessions in each, their model and effort, and the chains. The prompts to paste are in the project document "Session prompts for the Blazor program", which holds prompt text only. Written after reading [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) to [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa), the ten second-opinion comments of 2026-09-11, the second-opinion review document and the assessment document, and after checking the fork at `experiment/06-blazor` (main is 1cfde11d3).

## Terms

* **Stage**: one step of the move to Blazor, lettered A to H plus N. Each stage has its own sessions and ends in a review that decides whether the next stage starts.
* **Session**: one fresh Claude Code session working on one Linear issue, with its model and effort set before it starts and never switched.
* **Chain**: a sequence of sessions that runs back to back without the owner. A driver session launches each session headless in turn and stops at the first BLOCKED or stop condition. Chains are numbered R1 to R6. A session that needs the owner in the loop is interactive and is never part of a chain.
* **Hop**: a scheduled SDK upgrade session, X1 and X2, run inside whichever stage is active.

## 1. What the second opinion changes

The review accepted the product constraints (plain Blazor, FluentUI, functional parity, .NET 11) and did not answer the four decisions. It changed six things the 2026-09-11 plan relied on. Each is adopted below.

1. **The repository strategy was narrowed by a false necessity.** SDK selection is per build root, not per Git repository. A sibling build root with its own `global.json` pinned to SDK 11 can live in the same tree while `application/global.json` stays at 10.0.301 and React stays untouched during the move. Adopted as the transition mechanism, subject to a build proof (session B0b) rather than a paper decision. Verified 2026-09-13: the development container now has SDK 11.0.100-rc.1.26425.128 installed beside 10.0.301 (session B0a, Done).
2. **The CSP proof was aimed at the wrong directive.** `script-src-elem` is emitted separately without `strict-dynamic`, so element loads are governed by it; WebAssembly compilation is checked against `script-src` or `default-src` separately. Adopted: session B1 tests the actual response policy with positive and negative cases, and the "no weaker than today" claim is a judgement the stage B review makes on that evidence.
3. **The hosting plan conflated two shapes.** Substituting `index.html` is not request-rendered Razor with static form posts, server DI lifetimes and response-cookie propagation. Adopted: session B2 proves a real email and one-time-password journey through the gateway with antiforgery and cookies; a deliberate full-document navigation at the authentication boundary is permitted.
4. **The estimates do not reconcile.** The starter's phase table sums to 37 to 51 weeks before discovery against a 4 to 5 month headline, and the delivery history supports fast work under precise contracts, not a page rate. Adopted: month figures are replaced by sessions, the unit this team spends; the delivery plan (session B5) reconciles effort, elapsed time and critical path from measured sessions; and the phases the assessment ran as parallel tracks (component gaps, localization content, end-to-end tests) are dissolved into each surface slice so nothing is counted twice.
5. **The corpus must be validated on finished slices.** Adopted: rules are written from the foundation code as it lands (session C7), then three representative vertical slices (V1 to V3) are built with them and measured with `pp claude-usage` and findings per slice before stage D assumes team throughput.
6. **Three accepted items are larger than accepted.** The public-client PKCE path is an authorization server, the offline shell and push are not near-zero, and the shared client assembly is a real extraction because `Account.csproj` and `SharedKernel.csproj` pull in ASP.NET Core and EF Core. Handled by the owner's criterion in section 3.

Not adopted as written: the review's 8 to 10 month solo allowance is a human-engineer figure, not the planning unit here; the calibration comes from sessions V1 to V3.

## 2. The four decisions, recorded 2026-09-13

| Issue | Decision | What it fixes in the plan |
| -- | -- | -- |
| [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) product or demonstration | **Product, to maintain.** React is replaced: when the program is complete there is no React in this fork. | The edition is the frontend of this fork, not a sibling. `docs/BLAZOR.md` ("an alternative, not a successor") is rewritten as as-built at the end. Owner: the fork owner. Budget: sessions, measured per stage. Stop criterion: the stage B review (B4) returns no-go, or the three vertical slices of stage C are not delivered within the session count B5 sets. Supported capability set: the full-edition flow matrix, delivered stage by stage; security fixes taken from upstream's backend at the existing rebase cadence. |
| [EP-51](https://linear.app/etarapartners/issue/EP-51/decide-the-scope-of-v1-reduced-starter-or-full-edition) v1 scope | **The goal is the full edition**, reached in meaningful steps, with the freedom to do things differently along the way as long as the result is a solid, cost-efficient platform for SaaS products. | No permanent exclusions. Billing (through hosted Stripe flows; constraint 3 still applies) and the back office are later stages, not deferred maybes. The first step is the starter scope (stages C and D), because it is where the value is highest and it calibrates the workflow. |
| [EP-52](https://linear.app/etarapartners/issue/EP-52/decide-the-repository-strategy-given-the-net-11-base) repository and runtime | **.NET 11 RC1 now, move with the .NET 11 release, then .NET 12. Own fork, no waiting for upstream.** FluentUI Blazor is assumed to work on .NET 11 RC1 in a released or prerelease version. | During the move, a sibling `blazor/` build root on SDK 11 while `application/` stays on 10.0.301, so the backend runs on a supported runtime and React keeps building; at .NET 11 GA (hop X2) the whole tree moves to SDK 11 and the backend retargets to net11; .NET 12 in November 2027 is the next scheduled hop. Backend updates from upstream are taken at the existing cadence; after React is retired, upstream frontend commits are not taken. The FluentUI assumption is checked in B0b and reported, not assumed silently. |
| [EP-53](https://linear.app/etarapartners/issue/EP-53/decide-whether-the-blazor-rules-and-skills-corpus-is-funded) corpus | **Yes: write the Blazor rules and skills.** | Session C7 writes them from the C1 to C6 code; V1 to V3 validate them; the stage C review measures the result before stage D runs as team-lead task sets. |

## 3. The native-client criterion, applied

Owner's criterion: the core must be able to serve web and native clients on mobile and desktop; defer a capability if adding it later is practical; pull it in now if getting the core right requires it.

| Item | Applied | Where |
| -- | -- | -- |
| [EP-59](https://linear.app/etarapartners/issue/EP-59/multi-client-readiness-platform-neutral-client-layer-and-a-native) items 1, 2, 4, 6: platform-neutral contracts and typed client with an architecture test, bootstrap endpoint, shared localization, no business logic in Razor components | **Pull in.** These are the core; deferring them is what makes a native client expensive. | Stage C, sessions C2 to C5 and C7 |
| [EP-59](https://linear.app/etarapartners/issue/EP-59/multi-client-readiness-platform-neutral-client-layer-and-a-native) item 3: OAuth with PKCE for public clients, and [EP-60](https://linear.app/etarapartners/issue/EP-60/c8-spike-prove-the-public-client-authorization-boundary-and-session), its spike | **Split.** The design constraint is pulled in: C3 makes the bootstrap endpoint and every client-facing endpoint work with a bearer token and no cookie, proven by a test, and designs session state on the "unify at the authorization server" rule. The spike [EP-60](https://linear.app/etarapartners/issue/EP-60/c8-spike-prove-the-public-client-authorization-boundary-and-session) runs inside stage C (session C8) so its protocol map follows C3 while it is fresh. The implementation of code issuance, token endpoint and client registration is stage N, scheduled by the owner after stage C at the earliest, because it is additive once the core is bearer-capable and it is a new exposure that deserves its own review. | C3, C8, stage N |
| [EP-59](https://linear.app/etarapartners/issue/EP-59/multi-client-readiness-platform-neutral-client-layer-and-a-native) item 5: installable PWA with offline shell and push | **Split.** Installability (manifest, icons, standalone window, audit) in stage C. The offline shell and push move to stage E with [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa), which rehearses the release boundary they depend on. C1 and C3 keep per-user data out of the cached shell so the offline shell stays practical later. | C1, stage E |
| [EP-57](https://linear.app/etarapartners/issue/EP-57/f1-spike-billing-through-stripe-hosted-checkout-and-customer-portal): billing through hosted Stripe flows | **Stage F**, where it runs as the stage's first session. The review's finding that the backend is not unchanged (custom-mode Checkout sessions, no Portal session operation) is preserved on the issue. It does not block the delivery plan; B5 plans stage F at stage level. | Stage F |
| [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa): client and server version compatibility and PWA release recovery | **Stage E**, before the offline shell. | Stage E |

## 4. Working rules for every session

Same as the ai-usage-optimization project, plus three spike rules.

* One slice per fresh session; rooted at the repository on the stage's branch; started from the Linear issue; plan first; no commit, amend or push without an explicit instruction (for a session in a chain, the child prompt is that instruction for its one issue; nothing in a chain pushes or merges); attribution scan clean before any push.
* Model and effort set before the session starts and never switched. Default Opus 5, high. Fable 5.1 where the tables say so: security boundaries, the corpus, reviews, the delivery plan. Medium effort only for mechanical, fully specified work. In a chain the flags are `--model opus` for Opus 5, `--model fable` for Fable 5.1, and `--effort` as given.
* End with a receipt on the issue: files changed, checks run, the `/usage` line, or in a chain the `pp claude-usage` figures because `/usage` does not work headless. Full logs stay local.
* Spike rule 1: every spike record names the tested commit, exact SDK and package versions, publish and trimming settings, browsers, device, network and cache conditions, and lists failures and unverified cases.
* Spike rule 2: a spike that cannot meet its acceptance inside its time-box is reported as unresolved (BLOCKED), never implemented implicitly. That stops its chain; its one follow-up session is interactive, and the chain is restarted from the next session. A second unresolved result is a B4 matter.
* Spike rule 3: spike code lives under the `blazor/` root on the branch and is labelled as spike code; stage C productionises or replaces it. Whether stage B merges to `main` was decided in B5 on 2026-09-13: not now; stage C branches from the tip of `experiment/06-blazor`, and the merge comes with a stage review at the earliest (section 5, stage C).
* One session per working tree at a time. Two sessions wrote into the same tree twice in stage B (B0b and B1), each costing a stop and a repeated session. The driver and the owner start nothing in a tree where another session is active, and a session that finds uncommitted changes it did not make stops (constitution, principle 4).

Branches: stage B on `experiment/06-blazor` (exists; not merged to `main` at the end of stage B, owner decision 2026-09-13). Each later stage gets the next numbered `experiment/` branch and its own Linear project, created by session B6 through `create-prd`, so the one-project-per-branch convention holds and each stage review can end in a merge to `main`.

## 5. Stages, sessions and chains

### Overview

| Stage | Sessions | Chain | Interactive (never chained) | Owner gate before the stage starts |
| -- | -- | -- | -- | -- |
| A Decide | none | none | the four decisions | Done 2026-09-13 |
| B Prove | B0a to B6 | R1: B0b, B1, B2, B3, B4 (ran 2026-09-13; B4 said go) | B0a (Done), spike follow-ups, B5, B6 | none |
| C Foundation | 16: C1a, C1b, C2, C3a, C3b, C3 review, C4, C4b, C5, C6, C7, C8, V1, V2, V3, stage C review | R2: all of them | none | B6 done |
| D Surfaces | 8 team-lead task sets D1 to D8, stage D review | none | all | owner has read the stage C review |
| E Release-ready | 8: E1, E2, E2 review, E2b, E4, E3, E5, stage E review | R3: E1, E2, E2 review, E2b, E4 | E3, E5, stage E review | stage D done |
| F Billing | 6: F1, F1 review, F2, F2 review, F3, F4 | R4: all of them | none | stage E done; Stripe test-mode keys configured by the owner |
| G Back office | 5 team-lead task sets G1 to G5, stage G review | none | all | stage F done |
| H Retire React | 5: H1a, H1b, H2, H3, stage H review | R5: all of them | the merge to `main` | stage G done and the owner says go |
| N Native-capable authorization | 4: N1, N2, N3, stage N review, to be corrected by C8's protocol map | R6: all of them | none | scheduled by the owner, after stage C at the earliest |
| U Upstream update | U1, the first real upstream update, measured | added by the owner at the head of a chain | or run interactively | upstream has a commit the branch lacks |
| X Hops | X1, X2 (X3 recorded) | added by the owner at the head of the next chain | or run interactively | the owner confirms the release has shipped |

58 planned sessions or task sets after B5 (B6; 16 in C; 9 in D; 8 in E; 6 in F; 6 in G; 5 in H; 4 in N; U1, X1 and X2), 66 to 75 with the follow-up and split allowance of section 8. Stage B is done except B5 and B6; stage C was fixed by B5 on 2026-09-13; stages D to H and N are fixed at session level here and become projects at B6.

### How a chain driver reads this section

A chain driver reads only the stage whose Chain line names its chain. It needs three things from that stage, and each stage that has a chain carries them: a "Project and branch" line, an Issue column in the session table, and the Chain line with the sessions in order and the stop conditions. The Model, effort column gives the flags; a session named "review" runs as a REVIEW session. Stage B has all three now. Stages C, E, F, H and N get their Project and branch line and Issue column from session B6, or from whichever session later creates that stage's issues; until then their chains cannot start.

### Stage A: Decide. Done 2026-09-13.

Delivers: [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) to [EP-53](https://linear.app/etarapartners/issue/EP-53/decide-whether-the-blazor-rules-and-skills-corpus-is-funded) answered and the native criterion applied (sections 2 and 3).

### Stage B: Prove. 8 sessions on `experiment/06-blazor`.

Project and branch: project `blazor-edition`, branch `experiment/06-blazor`.

Delivers: a measured yes or no on whether this platform can host a Blazor frontend under the existing gateway, policy and authentication, and an executable delivery plan for stages C to H as standalone Linear issues. Aligns with the goal by turning the four unknowns the assessment flagged into numbers and rules before the foundation is built on them.

| Session | Issue | Slice | Delivers | Model, effort | Mode |
| -- | -- | -- | -- | -- | -- |
| B0a | [EP-72](https://linear.app/etarapartners/issue/EP-72/b0a-install-the-net-11-rc1-sdk-alongside-100301-in-the-development) | Development container: SDK 11 RC1 installed alongside 10.0.301 | Done 2026-09-13: 11.0.100-rc.1.26425.128 beside 10.0.301 | Opus 5, medium | interactive (the owner rebuilt the container) |
| B0b | [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers) | Sibling build root: `blazor/global.json`, solution, empty Blazor Web App (server host plus WebAssembly client) that builds, publishes and runs standalone; `pp build`, `format` and `lint` cover the new root and the React edition is unaffected; FluentUI Blazor and one chart library resolved on .NET 11 RC1 with versions and prerelease status recorded; go-live licence recorded; this document imported as `docs/blazor-run-plan.md` | [EP-52](https://linear.app/etarapartners/issue/EP-52/decide-the-repository-strategy-given-the-net-11-base)'s proof; the unification-at-GA plan written down | Opus 5, high | chain R1 |
| B1 | [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and) | Boot under the gateway with the proposed policy, payload measured | Aspire launch of the Blazor host by project path from the net10 AppHost; served through YARP with the shell machinery verdict; CSP effective-directive tests, positive and negative, against the real response headers in Chrome, Firefox and Playwright WebKit; trimmed release publish measured with the .NET 11 feature switches set, Brotli, fingerprinted assets, cold and warm, service worker disabled; numbers in a table | Opus 5, high | chain R1 |
| B2 | [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and) | Render mode split | Static SSR public surface with a real login, signup and one-time-password journey through the gateway, antiforgery enforced, response cookies propagated; the WebAssembly authenticated surface started once; a throwaway bootstrap endpoint demonstrating the [EP-59](https://linear.app/etarapartners/issue/EP-59/multi-client-readiness-platform-neutral-client-layer-and-a-native) contract in both modes including logout, expiry and tenant switch; the written render-mode rule, first entry of the corpus; where full-document navigation happens | Opus 5, high | chain R1 |
| B3 | [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11) | Data grid and capability map | Users list against the real API under the nonce-only style policy at realistic row counts; `FluentDataGrid` and `QuickGrid` compared with versions recorded; a capability-to-component mapping for the full edition's surfaces replacing the 10 to 15 figure, showcase components excluded explicitly | Opus 5, high | chain R1 |
| B4 | [EP-73](https://linear.app/etarapartners/issue/EP-73/b4-spike-verdict-review-and-the-go-or-no-go-for-the-foundation) | Stage B review | Each of B0a to B3 checked against its acceptance evidence; the CSP threat-model judgement; the go or no-go for stage C written on the project, with the corrections stage C must carry | Fable 5.1, xhigh | chain R1, review |
| B5 | [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes) | Delivery plan | The full-edition flow matrix from the 20 specification files, ordered into stages C to H; minimal operator recovery for the period before stage G; effort versus elapsed in sessions with the critical path; the upstream update rehearsal replayed and its cost recorded; hop schedule; delivery home for each [EP-59](https://linear.app/etarapartners/issue/EP-59/multi-client-readiness-platform-neutral-client-layer-and-a-native) item; the sessions and chains of stages C to H and N updated in this document; whether stage B merges | Fable 5.1, xhigh | interactive, after the owner has read the B4 verdict and done the real Safari check the container cannot run |
| B6 | [EP-74](https://linear.app/etarapartners/issue/EP-74/b6-create-the-foundation-project-and-its-issues-from-the-delivery-plan) | Stage projects and issues | The stage C Linear project and branch created through `create-prd` with the clarify step and `analyze-feature`; standalone issues in T-number order with milestones; stages D to H and N captured as projects with their session lists but not yet detailed | Opus 5, high | interactive, because the clarify step asks the owner questions |

**Chain R1:** B0b, B1, B2, B3, B4. Stops at the first BLOCKED, and after B4 when its verdict is no-go.

### Stage C: Foundation. 16 sessions on the next `experiment/` branch, own project.

Project and branch: created by session B6. The project comes from `create-prd`; the branch is the next numbered `experiment/` branch, taken from the tip of `experiment/06-blazor` (`4c5a7dc78`), because stage B does not merge to `main` (owner decision 2026-09-13). Until B6 has filled in this line and the Issue column, chain R2 cannot start.

Delivers: the Blazor edition as a running application behind the gateway with three complete user journeys, the shared contracts and typed client, bootstrap-based identity that works for a bearer-only client, feature flags and localization from C#, the developer CLI and CI covering it, the public-client protocol map, and the first corpus validated on those journeys. Aligns with the goal by making the core real for web and native alike: after stage C a user can sign up, log in, manage users and switch tenants in Blazor, and the workflow that builds the remaining surfaces has been measured. Fixed by B5 on 2026-09-13 from the B4 verdict (the comment on the project, everything checked at `4c5a7dc78`); "correction n" below is B4's numbering.

| Session | Issue | Slice | Delivers | Model, effort |
| -- | -- | -- | -- | -- |
| C1a | B6 | Shell and policy, productionised from the B1 and B2 arrangement | Static SSR shell with per-page render mode following `docs/blazor-render-mode-rule.md`; the FluentUI pin `5.0.0-preview.26254.1` kept with the `nuget.config` source mapping, the note that SemVer orders it below `rc.5` so the upgrade-packages skill must not move it, the exit criterion (a nuget.org release containing `c813a4a6b`) and the NU3042 decision recorded (correction 1); `base-uri 'none'` kept with the `document.baseURI` shim as production code, every rendered URL root-absolute through one helper, no path base as a literal in components, and a deeper-route interactive test in Chromium, Firefox and Playwright WebKit (2); brand tokens in an external stylesheet, no nonced inline element in `<head>`, and a test that one enhanced navigation produces 0 violations in Chromium (3); the ApexCharts nonce unconditional in the shell, or charts left out until stage G (4); `form-action 'self'` and `worker-src` evaluated, with the external-login redirects tested before `form-action` is kept (6); the spike-only `csp-variant`, `b1-hooks` and `b1-apex-nonce` switches and the stale `loginReturnPath` harness case deleted, not ported (14); the PWA manifest, icons and standalone window with an installability audit (EP-59 item 5, first half); no per-user data in the shell, so the offline shell of stage E stays practical | Opus 5, high |
| C1b | B6 | Public-page budget and trimming | The six public pages measured on a trimmed Release publish with Brotli through the gateway, medians of 7 samples with the TTI event defined (12); a public-page budget set, and the cost of keeping FluentUI's JS initializer (455,973 bytes uncompressed) and the scoped CSS bundle (171,567 bytes) off the public surface priced (7); `ILLink.Descriptors.xml` kept, a trimmed-publish smoke test that fires at least one FluentUI event, Brotli kept publish-only until X1 re-checks the Development static-asset defect (8); the Firefox warm load that is no faster than cold (B1 finding 5) explained or recorded as open | Opus 5, high |
| C2 | B6 | Contracts extraction | Platform-neutral `net10.0` contracts and client projects under `application/`, referenced across the build-root boundary, with an architecture test on the transitive dependency boundary that fails the build on a reference to a UI framework or the web host packages; the commands, responses, ids and the problem-details error shape that the three journeys need moved out of `Account.Core` and `SharedKernel`; the rule that every later slice extracts the contracts it needs the same way, so the extraction is never a phase of its own (EP-59 item 1, with C4) | Opus 5, high |
| C3a | B6 | Production authentication, keys and proxies | The direct user-secrets read and the `Localhost` issuer and audience replaced by SharedKernel's token signing service and validation parameters (verified at `4c5a7dc78`: the APIs validate through `SharedDependencyConfiguration.GetTokenSigningService().GetTokenValidationParameters(...)`); the host joins the cross-service data protection key ring (the local application name now, the Container Apps environment recorded for E3); known proxies configured for forwarded headers; the host recorded as a confidential client of the account API, and the call path, through the gateway or direct, decided and written down (5) | Opus 5, high |
| C3b | B6 | Bootstrap endpoint and authentication state | Identity, runtime configuration and system-scope flags from one endpoint that no client reads from injected HTML (EP-59 item 2); authentication state across both render modes; logout, expiry and tenant switch; every client-facing endpoint proven by a test to work with a bearer token and no cookie; session state designed on the "unify at the authorization server, not at the credential store" rule (EP-59 item 3, design constraint); the access-token lifetime recorded as the revocation bound (11) | Opus 5, high |
| C3 review | B6 | Identity boundary review of C3a and C3b | Findings on the C3 issues, blocking or not; the revocation bound and the call path judged | Fable 5.1, xhigh |
| C4 | B6 | Typed client, forms and flags | Typed `HttpClient` client over the contracts (EP-59 item 1, second half); `EditContext` and `ValidationMessageStore` bound to the problem-details error set through one shared mapper, with a toast for non-validation errors; the dirty-state guard for dialogs and page navigation; feature flags read from the C# registry and the `x-user-feature-flags` header | Opus 5, high |
| C4b | B6 | List foundation | One shared QuickGrid wrapper (9): the selection model with select-all, indeterminate state and clearing on page, sort or filter change; the row keyboard model; list URL state reconciled with QuickGrid's `?sort=`, `?direction=` and `?page=` through NavigationManager; a localized paginator; a server-page cache; the URL-state model settled here and validated in V2 before any list surface is built | Opus 5, high |
| C5 | B6 | Localization | Shared resource assembly referenced by the host and the client (EP-59 item 4); culture preserved across prerender; FluentUI's built-in texts through its localizer; the strings of the three journeys in en-US and da-DK | Opus 5, high |
| C6 | B6 | Developer CLI and CI | `pp build`, `test`, `format` and `lint` cover the Blazor root, with the `--all-files` question for `format` decided (B0b finding 4); `e2e` can target the edition; a CI job builds, publishes trimmed and runs the smoke test from C1b (8); the Aspire resource kept | Opus 5, high |
| C7 | B6 | Corpus seed | Rules written from the C1 to C6 code: structure, forms and validation, API access, flags, localization, CSP, render modes, and "no business logic in Razor components" with an example (EP-59 item 6); the stage B rules (10): render mode on the hosted component, never the router; host-container registration for everything prerendered; absolute hrefs through the helper; no `style-src-attr 'unsafe-inline'`, and components that write style attributes wrapped or avoided; FluentMenu items inside `FluentMenuList`; no `@onkeydown` on a large surface; plain delegates around provider calls; medians of repeated samples for any budget (12); the skills the three journeys need | Fable 5.1, high |
| C8 | [EP-60](https://linear.app/etarapartners/issue/EP-60/c8-spike-prove-the-public-client-authorization-boundary-and-session) | Public-client authorization spike, after C3 | Protocol map naming the authorization server, clients, audiences and scopes; a minimal harness doing S256 PKCE through the system browser against the C3 design; the negative cases EP-60 lists; the exact backend and gateway changes stage N will need, estimated in sessions, separately from discovery | Opus 5, high |
| V1 | B6 | Vertical slice: login, signup, one-time password, welcome | The B2 static SSR forms as production code with localized server validation; the resend-code form B2 left out; the plain-input one-time password (the six-slot input is stage D); the `@smoke` flows of `login-flows`, `signup-flows` and `landing-page-flows` re-pointed | Opus 5, high |
| V2 | B6 | Vertical slice: users list with role change and side pane | The B3 QuickGrid list rebuilt on the C4b wrapper against the real API under the nonce-only policy; role change and the side pane; the URL-state model validated; the role-management part of `user-management-flows` re-pointed | Opus 5, high |
| V3 | B6 | Vertical slice: tenant switch, logout, session expiry, profile | The authentication and tenant transition journey on C3b; profile read and edit; the single-tab part of `tenant-switching-flows` and the logout and expiry parts of `session-management-flows` re-pointed | Opus 5, high |
| Stage C review | B6 | Review and corpus validation | Findings per slice, BLOCKED stops and rework recorded, `pp claude-usage` per session against the calibration in section 8, the C8 verdict, whether stage D may assume agent-team throughput; the merge recommendation for `main` | Fable 5.1, xhigh |

**Chain R2:** every session in the table, in table order. Stops at the first BLOCKED, when the C3 review or the stage C review records blocking findings, and after the stage C review when it finds team throughput is not supported (stage D is then re-planned as pair-programmer sessions; section 8 gives the count).

Stop criterion from EP-50, made concrete: V1, V2 and V3 are delivered within 5 sessions (3 planned plus 2 follow-ups). A sixth is the stop criterion and goes to the owner.

Session sizing (B4 finding 13): C1, C3 and the list foundation are split so that measurement (C1b) and record-writing are separate from implementation, and no session is planned to carry more than one measurement correction. A session that passes 200k tokens of context writes its hand-off and stops; the follow-up is a new session that counts against the allowance of section 8.

V1 to V3 are the review's "representative slices". They are single sessions rather than team-lead task sets, so that the corpus is validated one session at a time before a team runs on it.

### Stage D: Surfaces. 8 team-lead task sets and a review, own project. Interactive.

Delivers: every user-facing surface of the starter scope in Blazor, each task set carrying its own component gaps from `docs/blazor-capability-map.md`, its strings and its re-pointed end-to-end flows from section 7. Aligns with the goal by completing the move for everything a tenant user touches. Models: role defaults (Opus 5, high for engineers, reviewers and the lead; guardian and regression tester at their pinned tiers), Fable 5.1 reviewers on any surface touching authentication or identity (D2, D4, D5), and a stage D review on Fable 5.1, xhigh. One interactive team-lead session per task set; not chained, because a team lead coordinates a team and needs approvals.

| Task set | Surfaces | Flows re-pointed (section 7) | Capability-map items carried |
| -- | -- | -- | -- |
| D1 App shell | Sidebar, navigation, mobile menu, banner slot, tenant switcher, user menu, PWA install prompt, theme, 404 and error pages | `global-ui-flows` | App shell (M), toasts and the global HTTP-error bridge (S), tooltip (S) |
| D2 Authentication completion | Google, Entra and MitID login and signup buttons with their callbacks and error pages; rate limiting, resend and code expiry; the six-slot one-time password input as a progressive enhancement of the static form | `login-flows` and `signup-flows` (`@comprehensive`, `@slow`), `google-oauth-flows`, `entra-oauth-flows`, `mitid-login-flows`, the login and signup halves of `localized-email-flows` | One-time password (L) |
| D3 Users administration | Invite dialog, delete one and many, filter dialog with badge and clear, recycle bin with restore, purge and empty, permission-based visibility and self-action rules, the list on phones | `user-management-flows` (deletion), `permission-based-ui-flows`, the users parts of `mobile-view-flows` | Row menus with long-press (M), filter dialog switch (S), confirmation dialogs (S), date range (M), toggle group (M) |
| D4 Profile and identity verification | Profile with avatar upload; MitID verification, the verified state and the refusal pages | `mitid-verification-flows` (the administrator revoke step stays on the React back office until stage G) | Upload wrapper (M), avatar (S) |
| D5 Preferences and sessions | Theme, language and zoom saved immediately; sessions page with revocation, the session-revoked error page, refresh-token replay; locale persistence across sessions; multi-tab tenant synchronization | `session-management-flows` (revocation, revoked elsewhere, replay), `localization-flows`, the multi-tab part of `tenant-switching-flows` | Preferences (S) |
| D6 Tenant settings and flags | Account settings with name and logo upload; the tenant-facing and user-facing feature flag controls in settings and preferences | the tenant and user parts of `feature-flag-flows` | Optimistic switch (S), number field (S) |
| D7 Public and product pages | Landing, terms, privacy and DPA as server-rendered Markdown, the home page that `main/WebApp` serves today | `landing-page-flows`; `federated-navigation-flows` re-pointed as plain navigation, then retired at H2 (section 7) | Markdown (S), relative dates (S) |
| D8 Mobile and responsive pass | Every surface at phone width, touch and long-press, narrow filter dialogs, keyboard paths | the rest of `mobile-view-flows` | Responsive column hiding (S), side pane full-screen mode (M) |

Order: D1 first, because every other surface renders inside it; D2 to D7 in any order with D3 before D8; D8 last, and it feeds E4's acceptance bar. If the stage C review does not support team throughput, each task set becomes two pair-programmer sessions in a chain R2b (16 sessions), with a Fable 5.1 review session after D2, D4 and D5.

### Stage E: Release-ready. 8 sessions, own project.

Delivers: a deployable and recoverable edition for the surfaces built so far. Aligns with the goal by making the edition usable in production before the larger surfaces are built, so the platform is exercised for real early.

| Session | Slice | Model, effort | Mode |
| -- | -- | -- | -- |
| E1 | Emails: the five React Email templates (verified at `4c5a7dc78`: `InviteUser`, `ResendEmailLogin`, `StartLogin`, `StartSignup` and `UnknownUser` under `application/account/WebApp/emails/templates/`, en-US and da-DK) to Razor components rendered with `HtmlRenderer`; `localized-email-flows` re-pointed; the Node email build removed from the Workers path | Opus 5, high | chain R3 |
| E2 | [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa): client and server version policy, release and rollback rehearsal, PWA recovery runbook | Opus 5, high | chain R3 |
| E2 review | Review of E2 | Fable 5.1, xhigh | chain R3, review |
| E2b | Offline shell and push notifications, with the acceptance evidence EP-59 specifies: tested Android and installed iOS versions, standalone launch offline, delivery and opening of a test push, permission denial and revocation, logout and tenant switch, no previous user's or tenant's data in the cached shell, public routes and external callbacks reaching the server online; the `worker-src` directive from C1a in place before the service worker registers | Opus 5, high | chain R3 |
| E4 | Accessibility and mobile acceptance bar, fed by D8; a real Safari run is an item here | Opus 5, high | chain R3 |
| E3 | Deployment: Bicep, Container Apps, gateway route, GitHub configuration; the Container Apps key ring from C3a; forwarded headers behind the Azure gateway verified (B2 finding 5 is an assumption until then); the React back office kept deployed (section 10) | Opus 5, high | interactive, after the owner approves Azure access |
| E5 | `docs/BLAZOR.md` interim as-built | Opus 5, high | interactive, after E3 |
| Stage E review | Review | Fable 5.1, xhigh | interactive, after E5 |

**Chain R3:** E1, E2, E2 review, E2b, E4. Stops at the first BLOCKED or blocking findings in the E2 review.

### Stage F: Billing. 6 sessions, own project.

Delivers: subscribe, upgrade, downgrade, cancel, reactivate, payment method update, invoices and billing history through Stripe hosted Checkout and Customer Portal, with the backend reconciliation kept. Aligns with the goal: the full edition, and the "not one to one" constraint applied where it saves the most.

| Session | Slice | Model, effort |
| -- | -- | -- |
| F1 | [EP-57](https://linear.app/etarapartners/issue/EP-57/f1-spike-billing-through-stripe-hosted-checkout-and-customer), hosted-flow spike; the review's evidence list applies: owner authorization, return-URL validation, VAT, failed payments, duplicated webhooks, post-redirect consistency | Opus 5, high |
| F1 review | Review of F1 | Fable 5.1, xhigh |
| F2 | Backend integration and contract changes the review found necessary: a Customer Portal session operation on `IStripeClient`, hosted-mode Checkout sessions, return-URL validation, the webhook reconciliation kept | Opus 5, high |
| F2 review | Review of F2 | Fable 5.1, xhigh |
| F3 | Billing pages: plan selection, current plan, billing information with country select, payment method, billing history; Owner-only access | Opus 5, high |
| F4 | `subscription-flows` re-pointed | Opus 5, high |

**Chain R4:** every session in the table, in table order. Stops at the first BLOCKED, at blocking review findings, and after F1 when it concludes hosted flows do not cover the plan-change matrix.

### Stage G: Back office. 5 team-lead task sets and a review, own project. Interactive.

Delivers: the back office in Blazor, replacing the React back office that section 10 keeps as the operator tool until then. Models: role defaults, Fable 5.1 review on the identity revocation surfaces (G3), stage G review on Fable 5.1, xhigh. Aligns with the goal: the full edition's operator side. Not chained, for the same reason as stage D. The components showcase is not ported (capability map).

| Task set | Surfaces | Flows re-pointed |
| -- | -- | -- |
| G1 Shell and dashboard | Back-office login with the back-office identity, shell, dashboard with the five chart cards on Blazor-ApexCharts under the unconditional nonce, view-all links | `back-office-flows` (`@smoke`) |
| G2 Accounts | Accounts list with filters, sort and the drift banner with reconcile; account detail with its tabs; cross-host requests rejected | `back-office-flows` (`@comprehensive`) |
| G3 Users | Users list; user detail with sessions, login history, feature flags, identity verification with revoke, and the A/B inclusion pin | the administrator revoke step of `mitid-verification-flows` |
| G4 Invoices and billing events | Both lists with view toggles and filters; billing events by type and account | `billing-events-flows` |
| G5 Feature flags | Flag list; flag detail with tenant and user overrides, rollout percentage, activate, deactivate and delete; the two lists on one page with prefixed URL parameters | `feature-flag-flows` (back-office parts) |

### Stage H: Retire React. 5 sessions, own project.

Delivers: the React frontends, the npm workspace, the module federation build and the Node plumbing in the developer CLI and Aspire host removed; the end-to-end suite's runner decided; `docs/BLAZOR.md` and the README as as-built; the feature-flag manifest generation and the TypeScript contract mirrors deleted. Aligns with the goal directly: this is the step at which there is no React in the fork.

| Session | Slice | Model, effort |
| -- | -- | -- |
| H1a | Remove the React applications: `application/account/WebApp`, `application/main/WebApp` and `application/account/BackOffice` sources, their gateway routes, the SPA fallback and shell template machinery, and module federation | Opus 5, high |
| H1b | Remove the npm workspace, Node from the developer CLI and the Aspire host (the `frontend-build` resource, the four `.esproj` shims, the development proxy), the feature-flag manifest generation, the TypeScript contract mirrors and the OpenAPI-to-TypeScript generation; `shared-webapp` goes with them except what H2 keeps | Opus 5, high |
| H2 | The end-to-end runner decided and applied: Playwright on Node kept for tests only, or a .NET runner; the whole suite green against Blazor for every flow in section 7 | Opus 5, high |
| H3 | `docs/BLAZOR.md` and the README as as-built; this document frozen with its as-built delta | Opus 5, high |
| Stage H review | Review; deletion is irreversible at the tree level | Fable 5.1, xhigh |

**Chain R5:** H1a, H1b, H2, H3, stage H review. Stops at the first BLOCKED or blocking findings. The merge to `main` is the owner's.

### Stage N: Native-capable authorization. 4 sessions, scheduled by the owner, after stage C at the earliest.

Delivers: the public-client path from C8's protocol map: client registration, authorization code issuance with S256 PKCE, token endpoint, audience and scope model, refresh and revocation, tenant switching for a bearer client, and the negative tests EP-60 lists. Aligns with the goal: the core serves native clients, without a native client being built. The sessions below are provisional until C8 has written the map; C8's estimate replaces them.

| Session | Slice | Model, effort |
| -- | -- | -- |
| N1 | Client registration, the audience and scope model, and the gateway and backend changes C8 named | Opus 5, high |
| N2 | Authorization code issuance with S256 PKCE and the token endpoint; the internal-only refresh routes proven inaccessible externally | Opus 5, high |
| N3 | Refresh, revocation and tenant switching for a bearer client; the negative tests: wrong verifier, reused or expired code, unregistered redirect, wrong audience, wrong client, cross-tenant authority; the compromised-web-session test | Opus 5, high |
| Stage N review | Identity boundary review | Fable 5.1, xhigh |

**Chain R6:** N1, N2, N3, stage N review. Stops at the first BLOCKED or blocking findings.

### Upstream updates

U1, Opus 5, high, interactive or at the head of a chain: the first upstream update taken after stage B, replayed onto the active stage's branch by rebase, with the conflicting files, hunks, minutes and the checks recorded on its issue. It is the measurement B5 could not take (section 9). Owner-triggered: upstream has a commit the branch lacks.

### Hops

X1 ([EP-75](https://linear.app/etarapartners/issue/EP-75/x1-move-the-blazor-build-root-from-net-11-rc1-to-rc2)), Opus 5, medium: RC1 to RC2, once RC2 has shipped. Verified in B0a on 2026-09-13: RC1 is go-live and no RC1 end-of-support date is published, so the trigger is the release, not a date (2026-10-13 remains an assumption). By the elapsed model of section 8 it falls inside stage C or D. X1 also re-checks the `Microsoft.Extensions.Http` 10.0.10 assembly in the WebAssembly payload, the tripled FluentUI assembly and the Development static-asset defect (correction 8 and the B0b findings). X2 ([EP-76](https://linear.app/etarapartners/issue/EP-76/x2-move-the-whole-tree-to-net-11-at-general-availability)), Opus 5, high: .NET 11 GA (expected 2026-11-10), including the tree unification on SDK 11 and the backend retarget to net11 per `docs/blazor-tree-unification.md`, whose FluentUI precondition is read as "the pinned version" rather than rc.5 (correction 14); by the elapsed model it falls inside stage E or later, and after stage H the unification has no React to keep. Package revalidation at X2: Aspire support for the .NET 11 runtime confirmed with evidence, the EF Core provider release, the FluentUI and ApexCharts assets re-resolved against GA, the Dockerfile image tags, the OpenAPI-generated TypeScript regenerated and reviewed while React still exists, and the end-to-end suite run. X3: .NET 12 in November 2027, recorded, not scheduled; its regression work is the X2 list again, minus whatever stage H removed. A chain never decides that a hop is due: the owner confirms the release has shipped and either adds the hop at the head of the next chain's session list or runs it interactively.

## 6. What stage B measured, folded in

Every input the 2026-09-11 draft said the spikes must supply, with the measured value and where it lands. Each spike's record names its tested commit, SDK and package versions, publish settings, browsers and conditions, failures and unverified cases in its receipt: B0a on [EP-72](https://linear.app/etarapartners/issue/EP-72/b0a-install-the-net-11-rc1-sdk-alongside-100301-in-the-development), B0b on [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers) (`ed297f915`), B1 on [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and) (`5c7b33124`), B2 on [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and) (`adabcad3a`), B3 on [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11) (`4c5a7dc78`), all reviewed by B4 on [EP-73](https://linear.app/etarapartners/issue/EP-73/b4-spike-verdict-review-and-the-go-or-no-go-for-the-foundation) at `4c5a7dc78`.

| Draft input | Measured | Lands in |
| -- | -- | -- |
| Payload "low single-digit megabytes compressed"; failure above roughly 5 MB unless the render-mode split rescues the public surface | 3,786,764 bytes Brotli transfer, 12,276,847 uncompressed, 64 requests in Chromium; TTI medians of 7 samples 581 ms cold and 426 ms warm, 4,066 ms cold on the throttled profile; da-DK 3,871,406 bytes. Public pages: 9 requests, no runtime, no preload, no circuit (B2, Development build, about 900 KB uncompressed, not yet measured on a Release publish). Neither half of the failure condition is met; nothing in the plan changes on this point | C1b sets the public-page budget on a Release publish; every budget in C and D uses medians (correction 12) |
| Component count "10 to 15 outside FluentUI" | 1 L, 13 M and about 25 S behaviours (capability map at `adabcad3a`), with 26 of 84 React components excluded as showcase-only; 30 to 60 engineer-days of behaviour work in the map's own unit, which this plan does not carry forward as days | C4, C4b and the D task sets, each carrying its items; the assumption rows (date range, toggle group, long-press, upload, one-time password) re-checked first by the task set that carries them |
| Render mode rule | `docs/blazor-render-mode-rule.md`: static SSR public surface, WebAssembly islands behind login, full-document navigation at five named boundaries | Every page in stages C, D and G; C7's corpus |
| Data grid | QuickGrid is the list foundation: 10,001 rows virtualized with 0 violations at 52 ms per three-viewport step; FluentDataGrid not adopted (428 to 434 `style-src-attr` violations per session, 2x to 5x slower, 100 range requests against 2); `style-src-attr 'unsafe-inline'` rejected; every timing a single run | C4b wrapper; V2 validates the URL-state model; correction 9 |
| Content Security Policy | No weaker than today: `'wasm-unsafe-eval'` added to `script-src`; `script-src-elem`, `connect-src` and `frame-src` narrower; the rest identical; conditions carried as corrections 1 to 4 and 6 | C1a |
| Corpus funded | Yes (EP-53); written from code, validated on slices | C7, V1 to V3, stage C review |
| Billing verdict | Not a plan input any longer; F1 opens stage F | Stage F |
| Multi-client readiness | Six items with delivery homes recorded on EP-59 | Section 11 |
| Real Safari | Not run in stage B (Playwright WebKit 26.5 stood in); the owner recorded on 2026-09-13 that a real Safari check is not critical for this plan | C1a's three-browser tests stay on Playwright WebKit; a real Safari run is an E4 acceptance item |

The 14 corrections of the B4 verdict, by session: 1, 2, 3, 4, 6 and 14 in C1a; 7, 8 and 12 in C1b (8 also in C6, 12 also in C7); 5 in C3a and E3; 11 in C3b; 9 in C4b and V2; 10 in C7; 13 in the session sizing of stage C; the X1 and X2 re-checks in the hops.

## 7. Flow matrix

Verified at `4c5a7dc78`: 20 specification files, 18 under `application/account/WebApp/tests/e2e/` and 2 under `application/main/WebApp/tests/e2e/`, 7,564 lines of specifications and 9,283 lines including the shared fixtures, tagged `@smoke`, `@comprehensive` and `@slow`. Each row names the retained behaviour and the stage and session that re-points it at Blazor. Until a flow is re-pointed it keeps running against React, which stays built and deployed until stage H. No exclusion is permanent (EP-51).

| Specification | Flows | Retained behaviour | Stage, session |
| -- | -- | -- | -- |
| `login-flows` | validation, security, authentication protection, logout; rate limiting; too-many-attempts message; resend after 30 s but not after expiry | all | C V1 (`@smoke`); D2 (`@comprehensive`, `@slow`) |
| `signup-flows` | validation, profile setup, account; too-many-attempts; resend and expiry | all | C V1 (`@smoke`); D2 (the rest) |
| `landing-page-flows` | landing with navigation for anonymous users; authenticated users redirected home | all | C V1 (landing); D7 (home) |
| `localized-email-flows` | localized signup and login one-time-password emails with the autocomplete attribute; transactional emails for login, unknown user and invite; resend emails | all; the UI half re-pointed early, the email assertions unchanged until the templates move | D2 (UI); E1 (emails) |
| `localization-flows` | language changes across signup, authentication and logout; persistence across sessions | all | C C5 and V1 (language on the public surface); D5 (persistence) |
| `session-management-flows` | sessions page with the current session and revocation; revoked from another browser; refresh-token replay | all | C V3 (expiry and logout); D5 (page, revocation, replay) |
| `tenant-switching-flows` | tenant switching and multi-tab synchronization | all | C V3 (switch); D5 (multi-tab) |
| `user-management-flows` | invitation, role management and permissions; single and bulk deletion with dashboard integration | all | C V2 (role management); D3 (invite, deletion) |
| `permission-based-ui-flows` | permission-based visibility, self-action restrictions, access-denied pages; bulk delete for Owners only | all | D3 |
| `mobile-view-flows` | mobile navigation and user management with keyboard accessibility; mobile forms and validation; mixed keyboard and mouse selection | all | D3 (users parts); D8 (the rest); E4 bar |
| `global-ui-flows` | theme switching with persistence across viewports; 404 page; error page | all | D1 |
| `google-oauth-flows`, `entra-oauth-flows` | signup, login, existing-user redirect, changed provider email; preferred tenant and error paths (skipped when the provider is not enabled) | all | D2 |
| `mitid-login-flows` | login with a verified identity recorded as MitID; unverified refused, return path carried, revoked stops working | all | D2 |
| `mitid-verification-flows` | verify and return to the profile; stale or weak authentication refused with the refusal pages; administrator revoke | all | D4 (verification); G3 (the revoke step; the React back office serves it until then, section 10) |
| `feature-flag-flows` | flags across back office, account settings and user preferences; filter and paginate tenants and users on the flag detail page | all | D6 (settings and preferences); G5 (back office) |
| `subscription-flows` | subscription lifecycle with plan changes and billing states; non-Owner denied; tabs, scheduled downgrade banner, billing info editing, payment history (skipped when Stripe is not configured) | the behaviour; the Stripe Elements forms, the payment iframe theming and the post-redirect polling hook are replaced by hosted flows per F1 | F F4 |
| `back-office-flows` | login, dashboard, tenant detail; drift banner, reconcile, tabs, accounts filter and sort, cross-host rejection | all | G1, G2 |
| `billing-events-flows` | list, filter by type and account, view-all from the dashboard | all | G4 |
| `federated-navigation-flows` | navigation between the main and account systems with proper route rendering | the user-visible navigation; the module federation mechanism has no Blazor equivalent | D7 (navigation), then retired as replaced at H2, not deferred |

Deliberate exclusions, all deferred or replaced, none permanent: the back-office components showcase (not a product flow and not in any specification; a Blazor showcase would be built for the components the edition builds); Stripe Elements-specific behaviour (replaced by hosted flows, decided by F1); the module federation navigation flow (replaced). Behaviour that exists in the product without a specification (for example the recycle bin's purge and the resend-code form) is carried by the task set of its surface, not by this matrix.

## 8. Sessions, calibration, elapsed time and the critical path

**Unit.** Effort is counted in sessions (one fresh session on one issue) and, for stages D and G, in team-lead task sets. Elapsed time is counted separately, in working days, because a chain's sessions run back to back but every stop, every interactive session and every task set waits for the owner.

**Calibration, measured.** `pp claude-usage --since 2026-09-13`, read on 2026-09-13 after B4; minutes are first call to receipt; list prices are the ones the report prints (Opus 5: cache read $0.50, 1-hour cache write $10, output $25 per million tokens; Fable 5.1: $0.25, $20, $50).

| Session | Model | Calls | Cache read | Output | Minutes | List cost | Median context | Calls over 200k |
| -- | -- | -- | -- | -- | -- | -- | -- | -- |
| B0b `846750ed` | Opus 5 | 116 | 22,226,815 | 104,447 | 23 | $18.5 | 198k | 49 % |
| B1 `09667015` with 3 sub-agents, BLOCKED | Opus 5 | 316 | 52,918,105 | 72,168 | 101 | $42.4 | 172k main, 209k largest agent | 16 % main, 53 % largest agent |
| B1 follow-up `df3a8b2e` | Opus 5 | 149 | 20,603,664 | 86,077 | 57 | $15.7 | 141k | 3 % |
| B2 `6c3a577a` | Opus 5 | 86 | 17,538,263 | 96,486 | 32 | $14.0 | 225k | 62 % |
| B3 `0d8a4ef8` with 1 sub-agent | Opus 5 | 201 | 54,560,424 | 150,777 | 63 | $37.4 | 332k | 81 % |
| B4 `5496a1f8` | Fable 5.1 | 13 | 2,251,405 | 62,209 | 12 | $9.3 | 220k | 62 % |

Stage B from B0b to B4: 6 sessions, 288 minutes, $137 at list price. The collided second B0b session (`03386cea`, 105 calls, about $13) is waste, not calibration.

What the calibration says:

* A spike-sized issue with a precise brief took 1.2 sessions (6 for 5) and would have taken about 1.8 had the three sessions over the 200k median been split as the constitution requires. Planning figure: **1.5 sessions per chained implementation session**, applied as a 20 % follow-up allowance plus a 30 % split allowance; reviews and C8 stay at 1.
* A chained implementation session runs **23 to 101 minutes, median 57**; a review session 12 minutes. A chain of six sessions is one working day when the owner clears its stops the same day: chain R1 ran from 10:43 to 17:42 with two owner stops totalling about two hours (the collision and the B1 decision).
* Cost at list price: **$14 to $42 per implementation session, $9 per review**, median $18.5; the highest figures come from sub-agents, which B1 and B3 used, and single-session issues cost $14 to $19.
* Team-lead task sets are not calibrated: none has run under this constitution. The stage C review measures V1 to V3 per session and decides whether stage D runs as task sets; the `ai-usage-optimization` project's figures are not reused here.

**Sessions per stage.**

| Stage | Planned | With allowance | Basis |
| -- | -- | -- | -- |
| B remaining | B6 | 1 | interactive |
| C | 16 (14 implementation including C7, C8 and V1 to V3; 2 reviews) | 21 to 23 | 1.5 on the 14, reviews at 1 |
| D | 8 task sets and 1 review | 8 to 9 task sets, or 16 sessions and 3 reviews as chain R2b | uncalibrated; gated by the stage C review |
| E | 8 (6 implementation, 2 reviews) | 10 to 11 | as C |
| F | 6 (4 implementation, 2 reviews) | 7 to 8 | as C; F1 may stop the chain |
| G | 5 task sets and 1 review | 5 to 6 task sets, or 10 sessions and 2 reviews | as D |
| H | 5 (4 implementation, 1 review) | 6 to 7 | as C |
| N | 4 (3 implementation, 1 review) | 5 to 6, replaced by C8's estimate | as C |
| U, X | U1, X1, X2 | 3 to 4 | X2 is one or two sessions (EP-76) |
| Total | 58 | 66 to 75 | |

**Elapsed time.** Assumptions, not measurements: the owner runs one chain or one interactive session at a time (two sessions in one tree collided twice in stage B) and clears stops within the same working day; interactive sessions and task sets run at one to two per working day.

| Stage | Sessions or task sets | Working days | Notes |
| -- | -- | -- | -- |
| B remaining | B6 | 1 | |
| C | 21 to 23 in chain R2 | 4 to 6 | about one hour per session; one to two stops per day expected (the C3 review, follow-ups) |
| D | 8 to 9 task sets | 8 to 14 | one task set per day, or two days when a task set needs a second pass; 16 to 19 sessions over 4 to 5 days as chain R2b |
| E | 10 to 11 | 4 to 6 | chain R3 in one to two days; E3, E5 and the review interactive |
| F | 7 to 8 | 2 to 3 | chain R4; the owner configures Stripe test mode first |
| G | 5 to 6 task sets | 5 to 9 | as D |
| H | 6 to 7 | 2 to 3 | chain R5; the merge is the owner's |
| N | 5 to 6 | 1 to 2 | off the critical path, whenever scheduled |
| U, X | 3 to 4 | 2 to 4 | inside the active stage; X2 needs the end-to-end suite |
| C to H with U and X | 60 to 68 | 27 to 45 | about 6 to 9 calendar weeks from B6 at daily owner availability, so stage H around late October to late November 2026 if B6 runs in mid September; X2 becomes due on 2026-11-10 at the earliest, which by this model is stage G, H or after, and if the program runs slower it falls earlier in the sequence |

**Critical path.** B5, B6, C1a, C2, C3a, C3b, C3 review, C4, C4b, C5, C7, V1, V2, V3, stage C review, D1, D2 to D7, D8, stage D review, E1, E2, E2 review, E2b, E4, E3, E5, stage E review, F1, F1 review, F2, F2 review, F3, F4, G1 to G5, stage G review, H1a, H1b, H2, H3, stage H review, the merge. Off the path: C1b (after C1a, before the stage C review), C6 (after C1a, any time before the stage C review), C8 (after C3b, any time before the stage C review), stage N, U1, X1 and X2 (inside whichever stage is active). Within a chain the order is the table order, so the off-path sessions cost chain time but never block a dependency.

**Reconciliation of the month figures.** None is carried forward; each maps to sessions as follows.

| Figure | Source | Covered | Reconciled to |
| -- | -- | -- | -- |
| Phase 2 foundation, 9 to 13 weeks | draft of 2026-09-11 | host, authentication, typed API, flags, localization, six multi-client items | stage C, 21 to 23 sessions, 4 to 6 working days, plus the EP-59 items whose homes are in E and N |
| Phase 3 corpus, 3 to 4 weeks | draft | a prose corpus before pages | C7, one session, plus V1 to V3 as validation; no prose phase |
| Phases 4, 6 and 7: components 4 to 6 weeks, strings 3 to 4, end-to-end 6 to 8 | draft | parallel tracks | dissolved into the D task sets, F4, the G task sets and H2; counted once, inside each slice |
| Phase 5 surfaces, 10 to 14 weeks | draft | user-facing surfaces | stage D, 8 to 9 task sets, 8 to 14 working days, uncalibrated until the stage C review |
| Phase 8 billing, 3 to 5 weeks | draft | hosted Stripe | stage F, 7 to 8 sessions |
| Phase 9 back office, 12 to 16 weeks | draft | full back office | stage G, 5 to 6 task sets |
| Phase 10 emails, 2 weeks | draft | React Email to Razor | E1, one session |
| Phase 11 polish, 6 to 8 weeks | draft | accessibility, mobile, performance | D8, E4 and C1b; the rest is inside each slice |
| Reduced starter 3 to 4 months; full v1 7 to 9 months for one engineer; parity 11 to 14 | assessment | human engineer-months | not a planning unit here; the session plan is 66 to 75 sessions or task sets and 6 to 9 calendar weeks under the elapsed assumptions above |
| 8 to 10 months solo allowance | second opinion | human engineer-months with risk | the same; the risk it priced is carried instead as the two uncalibrated stages (D and G), the stage C review gate and the stop criterion on V1 to V3 |
| 30 to 60 engineer-days of behaviour work | capability map | component and behaviour work before slices | C4, C4b and the items per task set; not added on top |
| 3 to 5 weeks multi-client; 5 engineering days for C8 | EP-59, EP-60 | the six items; the authorization spike | C2 to C5, C7, C8 (one session), E2b, stage N |

Double counting: localization, end-to-end re-pointing and review are inside each session or task set above and nowhere else; the only separate review sessions are the stage reviews and the four boundary reviews (C3, E2, F1, F2). Staffing assumption: one owner driving one session at a time; no parallel chains.

Where this plan is most likely wrong: stage D and G throughput (no task set has run under this constitution; the stage C review is the gate); the elapsed assumption of daily owner availability; and C2, the contracts extraction, which B0b's and B3's history says is the session most likely to exceed one, and whose scope is limited to the three journeys for that reason.

## 9. Upstream update rehearsal

Not run in B5. Verified 2026-09-13: `git fetch upstream` brought nothing; `upstream/main` is `269de1c18` (2026-07-06), which is the merge base of `experiment/06-blazor`, so no upstream commit exists that the branch lacks and nothing real could be replayed. The owner decided on 2026-09-13 to continue without a synthetic replay. The tracking cost therefore stays an assumption until U1, the first real upstream update, which is measured as section 5 describes.

Static evidence, which is the weaker "counted from touched files" kind the issue warned against, and is labelled as such: outside `blazor/`, `docs/` and `.claude/`, the four stage B commits changed eight files (`application/AppGateway/appsettings.json`, `application/AppGateway/Filters/ClusterDestinationConfigFilter.cs`, `application/AppHost/Program.cs`, `application/shared-kernel/SharedKernel/Configuration/PortAllocation.cs`, `developer-cli/Commands/BuildCommand.cs`, `developer-cli/Commands/FormatCommand.cs`, `developer-cli/Commands/LintCommand.cs` and `developer-cli/Installation/Configuration.cs`). Of the 45 most recent non-merge commits on `upstream/main` (2026-05-31 to 2026-07-06), one touches any of them: `6bdbd70f4`, "Route HMR WebSocket through the AppGateway", on `appsettings.json`. Assumption: the gateway route table, the AppHost and the developer CLI commands are where a real update will conflict, and each conflict is a few hunks in a configuration or command file, not a design conflict. What no static count shows is the cost of upstream commits after stage H, when `application/*/WebApp` no longer exists on the branch: upstream frontend commits are then not taken (EP-52), and a backend commit that also touches the React frontend is taken with its frontend hunks dropped. U1 records the first real figure.

## 10. Operator recovery before stage G

Verified at `4c5a7dc78`: the two operator actions the owner named already exist as back-office API endpoints, both on the back-office host behind the back-office identity policy, with the write actions behind the admin policy:

* Revoke a verification: `DELETE /api/back-office/users/{id}/identity-verification` (`application/account/Api/BackOffice/UsersEndpoints.cs`), with `GET /api/back-office/users/{id}/identity-verification` to read the state.
* Administer feature flags (`application/account/Api/BackOffice/FeatureFlagEndpoints.cs`): `GET /api/back-office/feature-flags`, `PUT .../{flagKey}/activate`, `PUT .../{flagKey}/deactivate`, `DELETE .../{flagKey}`, `PUT .../{flagKey}/rollout-percentage`, `PUT` and `DELETE .../{flagKey}/tenant-override`, `PUT` and `DELETE .../{flagKey}/user-override`, and the tenant and user lists per flag.

Supported procedure until stage G: the React back office (`application/account/BackOffice`) stays built, deployed and covered by `back-office-flows`, because stage H, which removes it, is gated on stage G being done. The operator tool for the period between the first Blazor deployment (E3) and the Blazor back office (G) is therefore the existing back office, unchanged; E3 keeps it deployed and X2 keeps it building. No minimal Blazor page is built. Fallback, if the React back office is ever unavailable before G: the endpoints above, called with a back-office identity on the back-office host, which is a supported procedure because the API's own authorization runs regardless of the client. Stage G's G3 and G5 replace both.

## 11. Hop schedule and the multi-client delivery homes

Hops, placed by the elapsed model of section 8 (assumptions until each release ships):

| Hop | Trigger | Expected placement | Work |
| -- | -- | -- | -- |
| X1 (EP-75) | .NET 11 RC2 ships; no RC1 end date is published (B0a); 2026-10-13 is an assumption | stage C or D | `blazor/global.json` and the container to RC2; FluentUI and ApexCharts re-resolved; the `Microsoft.Extensions.Http` 10.0.10 assembly, the tripled FluentUI assembly and the Development static-asset defect re-checked; build, format, lint and test on both roots |
| X2 (EP-76) | .NET 11 general availability, expected 2026-11-10 | stage E or later, depending on progress; after stage H the unification has no React to keep | one SDK in every `global.json`; backend and CLI retargeted to `net11.0`; `blazor/` folded into `application/` per `docs/blazor-tree-unification.md`, whose FluentUI precondition reads "the pinned version"; Aspire support for the .NET 11 runtime confirmed with evidence; the EF Core provider release; the Dockerfile image tags; the OpenAPI-generated TypeScript regenerated and reviewed while React exists; an Aspire restart and the end-to-end suite |
| X3 | .NET 12, expected November 2027 | after this program | recorded, not scheduled; the X2 list again, minus whatever stage H removed |

EP-59 items, delivery home and the outcome that closes each (from EP-59's "Done when" and its PWA acceptance evidence):

| Item | Home | Checkable outcome |
| -- | -- | -- |
| 1 Contracts and typed client in a platform-neutral assembly | C2, C4 | The projects exist under `application/` and an architecture test fails the build on a reference to a UI framework or the web host packages; the typed client is the only API path the Blazor client uses |
| 2 Bootstrap endpoint | C3b | One endpoint returns identity, runtime configuration and system-scope flags; the Blazor client reads nothing from injected HTML; a test calls it with a bearer token and no cookie |
| 3 OAuth with PKCE for public clients, cookies unchanged | C3b (design constraint), C8 (protocol map and harness), stage N (implementation) | C3b: every client-facing endpoint works bearer-only by test; C8: the map, and the harness's positive and negative results; N: the gateway accepts a registered public client with S256 PKCE while the cookie flow is unchanged and its tests still pass |
| 4 Localization in a shared assembly | C5 | Resources live in the shared assembly referenced by host and client; culture survives prerender; en-US and da-DK strings for the three journeys |
| 5 Installable PWA with offline shell and push | C1a (installability), E2 (release boundary), E2b (offline shell and push) | C1a: the installability audit passes; E2b: the evidence list of EP-59 recorded per tested Android and installed iOS version, including the negative cases and no previous user's data in the shell |
| 6 No business logic in Razor components | C7 | The rule is in the corpus with an example, and the stage C review finds V1 to V3 conform |

## 12. Linear record, applied 2026-09-13

Every edit kept the original text and added a dated section or comment, so the 2026-09-11 plan and the second opinion stay readable as the record.

* [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) to [EP-53](https://linear.app/etarapartners/issue/EP-53/decide-whether-the-blazor-rules-and-skills-corpus-is-funded): one comment each, "Decision recorded 2026-09-13"; status Done, milestone A Decide.
* Stage B issues, milestone B Prove, in order: [EP-72](https://linear.app/etarapartners/issue/EP-72/b0a-install-the-net-11-rc1-sdk-alongside-100301-in-the-development) B0a (Done), [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers) B0b (blocked by [EP-72](https://linear.app/etarapartners/issue/EP-72/b0a-install-the-net-11-rc1-sdk-alongside-100301-in-the-development)), [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and) B1 (blocked by [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers)), [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and) B2, [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11) B3, [EP-73](https://linear.app/etarapartners/issue/EP-73/b4-spike-verdict-review-and-the-go-or-no-go-for-the-foundation) B4 (blocked by [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and), [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and), [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11)), [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes) B5 (blocked by [EP-73](https://linear.app/etarapartners/issue/EP-73/b4-spike-verdict-review-and-the-go-or-no-go-for-the-foundation)), [EP-74](https://linear.app/etarapartners/issue/EP-74/b6-create-the-foundation-project-and-its-issues-from-the-delivery-plan) B6 (blocked by [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes)). [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and), [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and), [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11) and [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes) carry a "Revised 2026-09-13" section with the adopted corrections; package readiness moved from [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and) to [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers).
* Milestone Later stages: [EP-57](https://linear.app/etarapartners/issue/EP-57/f1-spike-billing-through-stripe-hosted-checkout-and-customer-portal) F1 (no longer blocks [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes)), [EP-60](https://linear.app/etarapartners/issue/EP-60/c8-spike-prove-the-public-client-authorization-boundary-and-session) C8, [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa) E2, [EP-59](https://linear.app/etarapartners/issue/EP-59/multi-client-readiness-platform-neutral-client-layer-and-a-native) with the delivery home per item, [EP-75](https://linear.app/etarapartners/issue/EP-75/x1-move-the-blazor-build-root-from-net-11-rc1-to-rc2) X1, [EP-76](https://linear.app/etarapartners/issue/EP-76/x2-move-the-whole-tree-to-net-11-at-general-availability) X2.
* Project: state Planned, a "Revised plan, 2026-09-13" section with the decisions, the stage and chain tables, model and effort policy and working rules. Stages C to H and N become their own projects at B6.
* Terminology, 2026-09-13: "chain" first named the lettered steps of the program and, separately, runs of back-to-back sessions. The lettered steps are now stages and "chain" means only a sequence of sessions run back to back; the project, milestone, issues, decision comments and both documents were corrected to match.
* This document is imported into the repository as `docs/blazor-run-plan.md` by session B0b ([EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers)), so it can be read without Linear. Later sessions update the repository copy and this document together.

* B5, 2026-09-13 ([EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes)): section 5 rewritten for stages C to N and its overview; sections 6 to 11 added; this section renumbered from 6 to 12; section 4 given the one-session-per-tree rule and the merge decision. The repository copy `docs/blazor-run-plan.md` was updated in the same session and left uncommitted for the owner's instruction, and the project's "Revised plan, 2026-09-13" section was given one sentence saying this document's session counts win. Owner decisions recorded the same day on EP-58: the real Safari check is not critical and is waived for this plan; the upstream update rehearsal is not run in B5 because upstream has no unmerged commit (U1 measures the first real one); stage B does not merge to `main` now.
