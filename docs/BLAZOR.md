# A Blazor frontend for PlatformPlatform

**The Blazor frontend is planned, not built.** Statements about the current product were verified against commit `1cfde11d3` on 2026-09-11. Estimates are marked as such.

If you are evaluating PlatformPlatform for its backend architecture, the frontend choice does not affect it.

## What is planned

The same PlatformPlatform core with a Blazor WebAssembly frontend instead of a React one.

Vertical slice architecture, DDD and CQRS, PostgreSQL, YARP, and the cookie-to-JWT path stay. Bicep, Azure Container Apps and the developer CLI remain the delivery foundation. Pages, components, forms, tables and charts move to C# and Razor. Node leaves the frontend build path.

The edition will use **.NET 11**, starting on RC1 and moving to general availability. As a spin-off, it is not bound by the React edition's SDK pin at 10.0.301. .NET 11 removes the Blazor CSP constraint described below, and its two-year support window ends only weeks before .NET 10's three-year window.

FluentUI will be the component foundation, not a reproduction of the existing design system. The goal is functional, not visual, parity.

This is an alternative, not a successor. PlatformPlatform classic remains the React reference implementation and receives new backend capabilities first.

## Who it is for

Teams that are already all-C# and regard a second language as a cost. If your team debugs, tests and reviews in .NET, this edition removes the React frontend from its remit.

If you have frontend specialists or want the deepest component ecosystem, choose PlatformPlatform classic. It will stay ahead.

## Advantages

* **One language, toolchain and debugger** - The application will be C#. A single debugging session can step from a Razor event handler into a command handler. It has one build, formatter, linter, test runner, dependency file and upgrade path.
* **One shared application layer** - Contracts, the typed API client, validation, localization and feature flag evaluation will live in a platform-neutral C# assembly for the web application and later .NET desktop or mobile clients. With TypeScript, a native .NET client must reimplement that layer or wrap an API shaped for another language.
* **No copied backend contracts** - Verified at `1cfde11d3`, 6,643 lines of generated TypeScript mirror OpenAPI documents. Four more mirrors are hand-maintained: `build/environment.d.ts` (206 lines) mirrors `UserInfo.cs`; `UnauthorizedReason` mirrors `AuthenticationMiddleware.ts`; `isValidReturnPath` mirrors `ReturnPathHelper`; and feature flags pass through JSON, Node and two generated TypeScript files. Blazor uses the same types directly, so renames fail at compile time.
* **Native server-side validation** - PlatformPlatform validates on the server and renders errors in the form. At `1cfde11d3`, React binds `mutation.error?.errors` into a custom validation context. Blazor expresses this directly through `EditContext` and `ValidationMessageStore`.
* **No frontend toolchain churn** - Verified at `1cfde11d3`, the classic build defers TypeScript 7 because the OpenAPI generator cannot load it, pins the Lingui SWC plugin because its API is not semver-stable, and defers React Compiler pending a bundler upgrade. Razor does not have these dependencies.
* **No Node in the repository** - After five transactional email templates move from React Email to Razor components rendered with `HtmlRenderer`, this also removes three of 16 development ports, the JavaScript Aspire resource, the development static proxy, four `.esproj` shims, and roughly 1,500 lines of npm and Playwright CLI plumbing.
* **No micro-frontend split** - Verified at `1cfde11d3`, the classic frontend composes three runtime bundles through module federation and forces React, the router, the i18n catalog and the query client to be singletons. The Blazor edition will be one application.
* **Feature flags are read, not regenerated** - The flag registry is already C#. The Blazor edition consumes it directly instead of emitting a manifest and generating a TypeScript union from it.
* **Shared .NET tests and isolated component tests** - Verified at `1cfde11d3`, the React frontend has no unit tests: roughly 53,600 production lines rely on 9,283 lines of Playwright end-to-end specifications, which CI does not run. The Blazor shared layer uses the backend's xUnit stack, and components can be tested in isolation. A business rule can have one test for its server enforcement and client presentation.
* **One team can own the product** - A .NET team can maintain the frontend without a second specialist discipline, review culture or hiring profile. This suits line-of-business software teams that would otherwise struggle to staff the frontend.
* **A smaller dependency surface** - Verified at `1cfde11d3`, the frontend declares 55 direct npm dependencies resolving to 710 lock-file packages. Estimated: an equivalent Blazor application's NuGet graph will be roughly an order of magnitude smaller and dominated by packages from one vendor with a published security process. NuGet has the same attack class; the benefit is fewer packages and suppliers to review.
* **Published support lifecycle for the whole stack** - .NET publishes dated support windows: three years for long-term releases and two for standard-term releases. React, the bundler, router and internationalization toolchain offer no equivalent commitment. This matters for software expected to run for a decade.

## Disadvantages

* **It starts behind and may stay behind** - At `1cfde11d3`, 37 of 76 non-merge commits in the preceding 90 days touched frontend code. Classic is actively developed. This edition receives no improvements automatically; every user-facing upstream capability needs manual porting. Adopt it only if late or no upstream tracking is acceptable.
* **The component ecosystem is thinner** - The plan uses FluentUI rather than reproducing the classic design system: 84 components on Base UI and Tailwind 4 at `1cfde11d3`. One-time-password inputs, drag-and-drop upload, a command palette, date-range picking, multi-select, resizable panels and Markdown rendering must be built or sourced. FluentUI has no charting components, so the dashboard needs a third-party Blazor chart library.
* **It will not look like PlatformPlatform classic** - The classic UI is a ShadCN-derived system with brand tokens and Apple Human Interface Guidelines control sizing. This edition will look like FluentUI. If you chose PlatformPlatform for the classic look, choose classic.
* **The initial download is larger** - Estimated, not measured: a trimmed Blazor WebAssembly payload with a component library will be low single-digit compressed megabytes, versus a few hundred kilobytes for React. Public pages will be server-rendered and WebAssembly starts after login. Measure this before considering it solved.
* **The Content Security Policy changes** - Verified at `1cfde11d3`, the classic policy is nonce-based with `strict-dynamic` and permits neither `unsafe-eval` nor `wasm-unsafe-eval`. This edition adds `'wasm-unsafe-eval'` for WebAssembly compilation and instantiation; it does not permit JavaScript `eval`. Stripe-hosted billing removes Stripe host allowances from `script-src-elem`, `connect-src` and `frame-src`, and removing the Node development server removes a development relaxation. The resulting policy is expected to be no weaker. .NET 11 also fixes a .NET 10-and-earlier issue where `Virtualize` emits inline spacer styles blocked by nonce-only `style-src`.
* **Localization is rebuilt** - Verified at `1cfde11d3`, classic has 1,599 unique messages in two Lingui ICU locales, merged at runtime across three layers. This edition uses .NET localization. With two locales and simple plural rules, this is a reasonable trade, but catalogs and translations must be rebuilt.
* **The AI rules corpus must be rewritten** - Much of PlatformPlatform's value is its rules and skills, refined over a year of daily use. The frontend portion encodes React, Lingui and bundler specifics and cannot be ported. Until an equivalent Blazor corpus exists, the multi-agent workflow will be less effective than in classic.

## What stays the same

The self-contained system layout, domain architecture, business behaviour and external identity integrations. The accepted multi-client foundation adds bootstrap and public-client authentication work below the API boundary. Hosting, build and deployment must accommodate Blazor artifacts and .NET 11. If billing is included, Stripe webhook handling and billing reconciliation will be reused and tested.

If you are evaluating PlatformPlatform for its backend architecture, the two editions are the same product.
