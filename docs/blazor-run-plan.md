# Blazor frontend: revised plan and run plan

Status: owner decisions recorded 2026-09-13 on [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) to [EP-53](https://linear.app/etarapartners/issue/EP-53/decide-whether-the-blazor-rules-and-skills-corpus-is-funded); the Linear record in section 6 is applied. This document is the one place that defines the stages, the sessions in each, their model and effort, and the chains. The prompts to paste are in the project document "Session prompts for the Blazor program", which holds prompt text only. Written after reading [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) to [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa), the ten second-opinion comments of 2026-09-11, the second-opinion review document and the assessment document, and after checking the fork at `experiment/06-blazor` (main is 1cfde11d3).

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
* Spike rule 3: spike code lives under the `blazor/` root on the branch and is labelled as spike code; stage C productionises or replaces it. Whether stage B merges to `main` is decided in B5.

Branches: stage B on `experiment/06-blazor` (exists). Each later stage gets the next numbered `experiment/` branch and its own Linear project, created by session B6 through `create-prd`, so the one-project-per-branch convention holds and each stage review can end in a merge to `main`.

## 5. Stages, sessions and chains

### Overview

| Stage | Sessions | Chain | Interactive (never chained) | Owner gate before the stage starts |
| -- | -- | -- | -- | -- |
| A Decide | none | none | the four decisions | Done 2026-09-13 |
| B Prove | B0a to B6 | R1: B0b, B1, B2, B3, B4 | B0a (Done), spike follow-ups, B5, B6 | none |
| C Foundation | C1 to C8, C3 review, V1 to V3, stage C review | R2: all of them | none | B6 done |
| D Surfaces | 8 to 12 team-lead task sets, stage D review | none | all | owner has read the stage C review |
| E Release-ready | E1 to E5, E2 review, stage E review | R3: E1, E2, E2 review, E4 | E3, E5, stage E review | stage D done |
| F Billing | F1 to F4, F1 review, F2 review | R4: all of them | none | stage E done; Stripe test-mode keys configured by the owner |
| G Back office | about 6 team-lead task sets, stage G review | none | all | stage F done |
| H Retire React | H1 to H3, stage H review | R5: all of them | the merge to `main` | stage G done and the owner says go |
| N Native-capable authorization | sessions from the C8 protocol map, stage N review | R6: all of them | none | scheduled by the owner, after stage C at the earliest |
| X Hops | X1, X2 | added by the owner at the head of the next chain | or run interactively | the owner confirms the release has shipped |

About 55 sessions or task sets in total. Stage B is fully specified; stage C is sketched for B5 to fix; stages D to H and N are stage-level until B6 writes their projects.

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

### Stage C: Foundation. About 13 sessions on the next `experiment/` branch, own project.

Delivers: the Blazor edition as a running application behind the gateway with three complete user journeys, the shared contracts and typed client, bootstrap-based identity that works for a bearer-only client, feature flags and localization from C#, the developer CLI and CI covering it, the public-client protocol map, and the first corpus validated on those journeys. Aligns with the goal by making the core real for web and native alike: after stage C a user can sign up, log in, manage users and switch tenants in Blazor, and the workflow that builds the remaining surfaces has been measured. Content fixed by B5; this is the sketch.

| Session | Slice | Delivers | Model, effort |
| -- | -- | -- | -- |
| C1 | Host and shell from the B1 and B2 arrangement, productionised | Static SSR shell with per-page render mode, CSP, Aspire resource, gateway route, installable PWA manifest; no per-user data in the shell | Opus 5, high |
| C2 | Contracts extraction | Platform-neutral net10 contract and client projects under `application/` with an architecture test on the transitive dependency boundary; commands, responses, ids and the problem-details error shape moved out of `Account.Core` and `SharedKernel` | Opus 5, high |
| C3 | Bootstrap endpoint and authentication state | Identity, runtime configuration and system-scope flags from one endpoint; auth state across both render modes; logout, expiry, tenant switch; every client-facing endpoint proven to work with a bearer token and no cookie; session state designed on the "unify at the authorization server" rule | Opus 5, high |
| C3 review | Identity boundary review of C3 | Findings on the C3 issue; blocking or not | Fable 5.1, xhigh |
| C4 | Typed client, forms and flags | Typed `HttpClient` client over the contracts; `EditContext` and `ValidationMessageStore` bound to the server error set; feature flags read from the registry and the `x-user-feature-flags` header | Opus 5, high |
| C5 | Localization | Shared resource assembly, culture preserved across prerender, the strings for the three journeys | Opus 5, high |
| C6 | Developer CLI and CI | `pp build`, `test`, `format`, `lint`, `e2e` cover the Blazor root; a CI job builds and publishes it | Opus 5, high |
| C7 | Corpus seed | Rules written from C1 to C6 code: structure, forms and validation, API access, flags, localization, CSP, render modes, no business logic in Razor components; the skills the three journeys need | Fable 5.1, high |
| C8 | [EP-60](https://linear.app/etarapartners/issue/EP-60/c8-spike-prove-the-public-client-authorization-boundary-and-session), public-client authorization spike | Protocol map naming the authorization server, clients, audiences and scopes; a minimal harness doing S256 PKCE through the system browser against the C3 design; negative cases; the exact backend and gateway changes stage N will need, estimated separately | Opus 5, high |
| V1 | Vertical slice: login, signup, one-time password, welcome | Localized server-validated forms, existing end-to-end flows re-pointed | Opus 5, high |
| V2 | Vertical slice: users list with role change and side pane | A list mutation on the grid from B3 | Opus 5, high |
| V3 | Vertical slice: tenant switch, logout, session expiry, profile | The authentication and tenant transition journey | Opus 5, high |
| Stage C review | Review and corpus validation | Findings per slice, BLOCKED stops and rework recorded, `pp claude-usage` per session, the C8 verdict, whether stage D may assume agent-team throughput; merge recommendation | Fable 5.1, xhigh |

**Chain R2:** every session in the table, in table order. Stops at the first BLOCKED, when the C3 review or the stage C review records blocking findings, and after the stage C review when it finds team throughput is not supported (stage D is then re-planned as pair-programmer sessions).

V1 to V3 are the review's "representative slices". They are single sessions rather than team-lead task sets, so that the corpus is validated one session at a time before a team runs on it.

### Stage D: Surfaces. 8 to 12 team-lead task sets, own project. Interactive.

Delivers: every user-facing surface of the starter scope in Blazor: preferences, sessions, tenant settings, full users administration including filter dialog, invite and recycle bin, the public and legal pages, and the mobile and responsive behaviour of each; each task set carries its own component gaps, strings and re-pointed end-to-end flows. Aligns with the goal by completing the move for everything a tenant user touches. Models: role defaults (Opus 5, high for engineers, reviewers and the lead; guardian and regression tester at their pinned tiers), Fable 5.1 reviewers on any surface touching authentication or identity, and a stage D review on Fable 5.1, xhigh. One interactive team-lead session per milestone; not chained, because a team lead coordinates a team and needs approvals.

### Stage E: Release-ready. About 7 sessions, own project.

Delivers: a deployable and recoverable edition for the surfaces built so far. Aligns with the goal by making the edition usable in production before the larger surfaces are built, so the platform is exercised for real early.

| Session | Slice | Model, effort | Mode |
| -- | -- | -- | -- |
| E1 | Emails from React Email to Razor | Opus 5, high | chain R3 |
| E2 | [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa): client and server version policy, release and rollback rehearsal, PWA recovery runbook, then the offline shell and push | Opus 5, high | chain R3 |
| E2 review | Review of E2 | Fable 5.1, xhigh | chain R3, review |
| E4 | Accessibility and mobile acceptance bar | Opus 5, high | chain R3 |
| E3 | Deployment: Bicep, Container Apps, gateway route, GitHub configuration | Opus 5, high | interactive, after the owner approves Azure access |
| E5 | `docs/BLAZOR.md` interim as-built | Opus 5, high | interactive, after E3 |
| Stage E review | Review | Fable 5.1, xhigh | interactive, after E5 |

**Chain R3:** E1, E2, E2 review, E4. Stops at the first BLOCKED or blocking findings in the E2 review.

### Stage F: Billing. About 6 sessions, own project.

Delivers: subscribe, upgrade, downgrade, cancel, reactivate, payment method update, invoices and billing history through Stripe hosted Checkout and Customer Portal, with the backend reconciliation kept. Aligns with the goal: the full edition, and the "not one to one" constraint applied where it saves the most.

| Session | Slice | Model, effort |
| -- | -- | -- |
| F1 | [EP-57](https://linear.app/etarapartners/issue/EP-57/f1-spike-billing-through-stripe-hosted-checkout-and-customer-portal), hosted-flow spike; the review's evidence list applies: owner authorization, return-URL validation, VAT, failed payments, duplicated webhooks, post-redirect consistency | Opus 5, high |
| F1 review | Review of F1 | Fable 5.1, xhigh |
| F2 | Backend integration and contract changes the review found necessary | Opus 5, high |
| F2 review | Review of F2 | Fable 5.1, xhigh |
| F3 | Billing pages | Opus 5, high |
| F4 | End-to-end flows | Opus 5, high |

**Chain R4:** every session in the table, in table order. Stops at the first BLOCKED, at blocking review findings, and after F1 when it concludes hosted flows do not cover the plan-change matrix.

### Stage G: Back office. About 6 team-lead task sets, own project. Interactive.

Delivers: dashboard with a third-party chart library, accounts, users, invoices, billing events and feature flag administration, replacing the minimal operator recovery from B5. Models: role defaults, Fable 5.1 review on the identity revocation surfaces, stage G review on Fable 5.1, xhigh. Aligns with the goal: the full edition's operator side. Not chained, for the same reason as stage D.

### Stage H: Retire React. About 4 sessions, own project.

Delivers: the React frontends, the npm workspace, the module federation build and the Node plumbing in the developer CLI and Aspire host removed; the end-to-end suite's runner decided (Playwright on Node retained, or a .NET runner); `docs/BLAZOR.md` and the README as as-built; the feature-flag manifest generation and the TypeScript contract mirrors deleted. Aligns with the goal directly: this is the step at which there is no React in the fork.

**Chain R5:** H1, H2, H3 (Opus 5, high), stage H review (Fable 5.1, xhigh, because deletion is irreversible at the tree level). Stops at the first BLOCKED or blocking findings. The merge to `main` is the owner's.

### Stage N: Native-capable authorization. Scheduled by the owner, after stage C at the earliest.

Delivers: the public-client path from C8's protocol map: client registration, authorization code issuance with S256 PKCE, token endpoint, audience and scope model, refresh and revocation, tenant switching for a bearer client, and the negative tests [EP-60](https://linear.app/etarapartners/issue/EP-60/c8-spike-prove-the-public-client-authorization-boundary-and-session) lists. Aligns with the goal: the core serves native clients, without a native client being built.

**Chain R6:** the implementation sessions B5 derives from C8 (Opus 5, high), then the stage N review (Fable 5.1, xhigh, identity boundary). Stops at the first BLOCKED or blocking findings.

### Hops

X1 ([EP-75](https://linear.app/etarapartners/issue/EP-75/x1-move-the-blazor-build-root-from-net-11-rc1-to-rc2)), Opus 5, medium: RC1 to RC2, once RC2 has shipped. Verified in B0a on 2026-09-13: RC1 is go-live and no RC1 end-of-support date is published, so the trigger is the release, not a date. X2 ([EP-76](https://linear.app/etarapartners/issue/EP-76/x2-move-the-whole-tree-to-net-11-at-general-availability)), Opus 5, high: .NET 11 GA (expected 2026-11-10), including the tree unification on SDK 11 and the backend retarget to net11. X3: .NET 12 in November 2027, recorded, not scheduled. A chain never decides that a hop is due: the owner confirms the release has shipped and either adds the hop at the head of the next chain's session list or runs it interactively.

## 6. Linear record, applied 2026-09-13

Every edit kept the original text and added a dated section or comment, so the 2026-09-11 plan and the second opinion stay readable as the record.

* [EP-50](https://linear.app/etarapartners/issue/EP-50/decide-is-the-blazor-edition-a-product-to-maintain-or-a-demonstration) to [EP-53](https://linear.app/etarapartners/issue/EP-53/decide-whether-the-blazor-rules-and-skills-corpus-is-funded): one comment each, "Decision recorded 2026-09-13"; status Done, milestone A Decide.
* Stage B issues, milestone B Prove, in order: [EP-72](https://linear.app/etarapartners/issue/EP-72/b0a-install-the-net-11-rc1-sdk-alongside-100301-in-the-development) B0a (Done), [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers) B0b (blocked by [EP-72](https://linear.app/etarapartners/issue/EP-72/b0a-install-the-net-11-rc1-sdk-alongside-100301-in-the-development)), [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and) B1 (blocked by [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers)), [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and) B2, [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11) B3, [EP-73](https://linear.app/etarapartners/issue/EP-73/b4-spike-verdict-review-and-the-go-or-no-go-for-the-foundation) B4 (blocked by [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and), [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and), [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11)), [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes) B5 (blocked by [EP-73](https://linear.app/etarapartners/issue/EP-73/b4-spike-verdict-review-and-the-go-or-no-go-for-the-foundation)), [EP-74](https://linear.app/etarapartners/issue/EP-74/b6-create-the-foundation-project-and-its-issues-from-the-delivery-plan) B6 (blocked by [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes)). [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and), [EP-55](https://linear.app/etarapartners/issue/EP-55/b2-prove-the-render-mode-split-server-rendered-public-surface-and), [EP-56](https://linear.app/etarapartners/issue/EP-56/b3-fluentdatagrid-and-quickgrid-against-the-real-users-list-on-net-11) and [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes) carry a "Revised 2026-09-13" section with the adopted corrections; package readiness moved from [EP-54](https://linear.app/etarapartners/issue/EP-54/b1-boot-blazor-webassembly-under-the-gateway-with-the-proposed-csp-and) to [EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers).
* Milestone Later stages: [EP-57](https://linear.app/etarapartners/issue/EP-57/f1-spike-billing-through-stripe-hosted-checkout-and-customer-portal) F1 (no longer blocks [EP-58](https://linear.app/etarapartners/issue/EP-58/b5-write-the-delivery-plan-for-the-full-edition-after-the-spikes)), [EP-60](https://linear.app/etarapartners/issue/EP-60/c8-spike-prove-the-public-client-authorization-boundary-and-session) C8, [EP-61](https://linear.app/etarapartners/issue/EP-61/e2-define-and-rehearse-clientserver-version-compatibility-and-pwa) E2, [EP-59](https://linear.app/etarapartners/issue/EP-59/multi-client-readiness-platform-neutral-client-layer-and-a-native) with the delivery home per item, [EP-75](https://linear.app/etarapartners/issue/EP-75/x1-move-the-blazor-build-root-from-net-11-rc1-to-rc2) X1, [EP-76](https://linear.app/etarapartners/issue/EP-76/x2-move-the-whole-tree-to-net-11-at-general-availability) X2.
* Project: state Planned, a "Revised plan, 2026-09-13" section with the decisions, the stage and chain tables, model and effort policy and working rules. Stages C to H and N become their own projects at B6.
* Terminology, 2026-09-13: "chain" first named the lettered steps of the program and, separately, runs of back-to-back sessions. The lettered steps are now stages and "chain" means only a sequence of sessions run back to back; the project, milestone, issues, decision comments and both documents were corrected to match.
* This document is imported into the repository as `docs/blazor-run-plan.md` by session B0b ([EP-77](https://linear.app/etarapartners/issue/EP-77/b0b-sibling-blazor-build-root-on-sdk-11-that-the-developer-cli-covers)), so it can be read without Linear. Later sessions update the repository copy and this document together.
