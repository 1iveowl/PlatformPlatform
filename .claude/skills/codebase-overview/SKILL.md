---
name: codebase-overview
description: Invoke first, before ls or grep, for where code lives or would live, which system owns a feature, or adding an OAuth provider, tab or endpoint.
---

# Codebase Overview

Verified at 0982adae1 (2026-09-12); the Blazor rows and patterns at 21ef926d8 (2026-09-15). Use `ls`, LSP and the
path-scoped rules in `.claude/rules/` for detail; this skill holds only what they cannot tell you. Terms: [references/glossary.md](references/glossary.md).

## Routing

| Where | Owns |
| --- | --- |
| `application/account/` | Identity, tenants, users and invitations, email and external login, sessions, subscriptions, billing, feature flags, back office |
| `application/main/` | The product slot: host SPA that consumes account's federated modules; no features yet |
| `application/<scs>/{Api,Core,Workers,Tests,WebApp}` | Endpoints, features and integrations, migrations host, xunit tests, React SPA |
| `application/account/BackOffice/` | Admin SPA; tabs are folders under `routes/`, menu in `shared/components/BackOfficeSideMenu.tsx`, API in `account/Api/BackOffice/` |
| `application/shared-kernel/SharedKernel/` | CQRS, domain, persistence, auth, OpenID Connect, feature flags, telemetry, port allocation |
| `application/shared-webapp/` | `@repo/ui`, `@repo/infrastructure`, `@repo/build`, shared Playwright setup |
| `application/AppHost/`, `application/AppGateway/` | Aspire orchestration and parameters; YARP proxy that turns auth cookies into bearer tokens |
| `developer-cli/` | The `pp` CLI behind the build, test, format, lint and e2e skills |
| `blazor/` | Blazor edition build root on its own SDK 11 `global.json`, rules in `.claude/rules/blazor/`; `--blazor` target of build, format, lint, test and e2e |
| `blazor/Blazor.Host/` | The server: static public pages in `Components/Pages/Public/`, authenticated pages in `Components/Pages/App/`, Development-only fixtures in `Components/Pages/Development/`, layout in `Components/Layout/`, headers, policy, brand, manifest and the prerender bootstrap adapter in `Shell/`, token authentication and the typed-client credential adapter in `Account/`, `wwwroot/app.css` |
| `blazor/Blazor.Client/` | The WebAssembly client: bootstrap, navigator and client registration in `Bootstrap/`, the shared form mapper, toast, dialogs and guard in `Forms/`, the `DataList` wrapper in `Components/Lists/`, culture in `Localization/`, one folder per feature (`Users/`), fixture components in `Development/`, JavaScript modules in `wwwroot/js/` |
| `blazor/Blazor.Tests/`, `blazor/tests/` | xunit tests by area (`Account/` through `HostFixture`, `Client/`, `Client/Lists/`, `Localization/`, `Shell/`); browser harness scripts `*.mjs` with `support/`, run by `blazor-harness`; end-to-end specs in `e2e/*-flows.spec.ts` with `e2e/support/` adapters, run by `e2e --blazor` |
| `application/account/Contracts/`, `application/account/Client/` | Platform-neutral account API contracts (`Features/<Feature>/{Requests,Queries,Domain}`) and typed clients (`<Area>Client.cs`, `AccountApiRoutes.cs`, `ApiCallResult`, `FeatureFlagState`, header handlers), referenced by the Blazor client and host |
| `application/shared-kernel/SharedKernel.Localization/` | Supported cultures and the en-US and da-DK `.resx` strings behind the generated `CommonStrings`, `AuthenticationStrings`, `UsersStrings`, `AccountStrings` and `FluentComponentStrings` classes |
| `cloud-infrastructure/` | Bicep and bash: `environment/`, `cluster/`, `modules/` |

Inside `Core/Features/<Feature>/`: `Commands/`, `Queries/`, `Domain/` (aggregate, types, EF configuration,
repository), `Shared/`. Integrations sit in `Core/Integrations/`.

There is no generated project dependency graph in this repository; derive project references from `PlatformPlatform.slnx`
and the `.csproj` files.

## Patterns to copy, not invent

- **External provider**: mirror Google, Entra or MitId. Provider class in
  `account/Core/Integrations/OAuth/<Provider>/` implementing `IOAuthProvider`; keyed registration plus a
  `mock-<provider>` registration in `account/Core/Configuration.cs`; a value in `ExternalProviderType`
  (`Features/ExternalAuthentication/Domain/ExternalAuthenticationTypes.cs`); an AppHost parameter block with
  `OAuth__<Provider>__*` environment variables; a `SystemFeatureFlag` in `SharedKernel.Contracts/FeatureFlags/FeatureFlags.cs`;
  Bicep parameters and Key Vault secrets under `cloud-infrastructure/cluster/`; entries in
  `developer-cli/Commands/GithubConfigCommand.cs`; login and signup buttons; tests in
  `account/Tests/ExternalAuthentication/`.
- **Endpoint**: implement `IEndpoints`, `MapGroup(...).WithTags(...).RequireAuthorization()`, one-line handlers that
  `await mediator.Send(...)`, `.AllowAnonymous()` only for public routes.
- **Pipeline**: Validation, Handler, PublishDomainEvents, UnitOfWork, PublishTelemetryEvents (registered in
  `SharedKernel/Configuration/SharedDependencyConfiguration.cs`).
- **Command file**: record, validator and handler in one sealed-class file with primary constructors, returning
  `Result<T>`.
- **Strongly typed id**: `[IdPrefix("usr")] public sealed record UserId(string Value) : StronglyTypedUlid<UserId>(Value)`;
  an architecture test fails without the prefix.
- **Repository**: interface extends `ICrudRepository`, class extends `RepositoryBase`, Scrutor registers it. Tenant
  scoping is an EF query filter; methods that bypass it end in `UnfilteredAsync`.
- **Migration**: hand written in `Core/Database/Migrations/YYYYMMDDHHmmss_Name.cs`, Up only, snake_case, `text`,
  `timestamptz`, `jsonb`. Never produced by EF tooling.
- **Telemetry event**: sealed class deriving `TelemetryEvent` in `Features/TelemetryEvents.cs`, past tense name,
  collected in the handler.
- **Backend test**: derive from `EndpointBaseTest<AccountDbContext>`, seed with `DatabaseSeeder`, assert telemetry
  through the collector spy.
- **Frontend route**: `createFileRoute` with `staticData.trackingTitle`, guards from
  `shared-webapp/infrastructure/auth/routeGuards.ts`, Lingui translations, `api.useQuery` and `api.useMutation`.
- **Blazor authenticated page**: a server-rendered page in `blazor/Blazor.Host/Components/Pages/App/` with `[Authorize]`,
  `[InteractiveSurface]` and a `Blazor.Client` component with `@rendermode="InteractiveWebAssembly"` (`AppDetails.razor`);
  a list prerenders its frame and loads its data in the browser (`UsersPage.razor`). Never a render mode on `Routes`.
- **Blazor public static form**: a page in `blazor/Blazor.Host/Components/Pages/Public/` mirroring `Login.razor` (enhanced,
  stays public) or `LoginVerify.razor` (not enhanced, crosses the authentication boundary): `EditForm` with `FormName`,
  `[SupplyParameterFromForm]`, `DataAnnotationsValidator`, the model in `PublicForms.cs`, `FormErrorMapper` and `FormErrorAlert`.
- **Blazor list page**: `DataList<TItem>` from `blazor/Blazor.Client/Components/Lists/` in a feature component plus a
  `<Feature>ListSource` (`Users/UsersListSource.cs`) and a host page (`Pages/App/UsersPage.razor`); never QuickGrid directly.
- **Blazor API call**: a contract in `application/account/Contracts/Features/<Feature>/{Requests,Queries}/`, a path in
  `application/account/Client/AccountApiRoutes.cs`, a method on `application/account/Client/<Area>Client.cs`, called from the
  component or list source with a `CancellationToken`; failures through `FormErrorMapper` (static) or `ApiFailurePresenter` (interactive).
- **Blazor string**: a key in `application/shared-kernel/SharedKernel.Localization/Resources/<Group>Strings.resx` and
  `<Group>Strings.da-DK.resx`, used as `@CommonStrings.Key`; validation messages through `ErrorMessageResourceType`.
- **Blazor tests**: xunit in `blazor/Blazor.Tests/<Area>/` (host behaviour through `Account/HostFixture.cs`, client code in
  `Client/`); an end-to-end spec in `blazor/tests/e2e/<feature>-flows.spec.ts` on the adapters in `blazor/tests/e2e/support/`;
  a browser harness case in `blazor/tests/<name>.mjs` for policy, navigation, keyboard, culture and timing.

## Gotchas

- The `pp` CLI refuses to run outside this repository's git root, and the bash hook blocks `cd`; use the skills.
- Google's redirect allowlist needs literal `https://localhost:<base port>`; `AppGateway/Middleware/LocalhostRedirectMiddleware.cs`
  bounces back to `app.dev.localhost` so state cookies survive.
- `ExternalAvatarClient` downloads avatars only from `.googleusercontent.com` and `.gravatar.com`.
- All worktrees share one AppHost user secrets store: `UserSecretsId` is tracked in `application/AppHost/AppHost.csproj`.
  Changing an OAuth or Postgres secret for one worktree changes it for all.
- No GitHub workflow runs Playwright, so the end-to-end suite is ungated and can be red on main. Run the baseline
  before blaming a branch.
- The Postgres container survives Aspire restarts and `email_logins` accumulates, so repeated full e2e runs exhaust
  one-time-password attempts. Prefer sequential single-browser runs.
- `.claude/commands/` no longer exists, but `SyncAiRulesAndWorkflowsCommand.cs` still reads it (a no-op).
- `developer-cli/Program.cs` special-cases an `mcp` command that does not exist; only `mcp-setup` does.
- `.github/dependabot.yml` is a stub; package updates go through the upgrade-packages skill.
- `.editorconfig` exists in `application/` and `developer-cli/`, not at the root.
- A `blazor-host` started before `build --blazor` serves stale framework files and the WebAssembly runtime fails to start;
  restart with the aspire-restart skill after the build. `update-packages` rewrites `blazor/Directory.Packages.props` too, and
  its FluentUI pin must not move: pass `--exclude Microsoft.FluentUI.AspNetCore.Components` (rule `.claude/rules/blazor/component-library.md`).
