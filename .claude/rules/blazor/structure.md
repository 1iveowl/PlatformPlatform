---
paths: blazor/**,application/account/Contracts/**,application/account/Client/**,application/shared-kernel/SharedKernel.Localization/**
description: Where Blazor pages, components, services, JavaScript modules, contracts, typed clients, strings and tests live in the Blazor build root and the shared projects
---

# Structure

Where code goes in the Blazor edition. The build root `blazor/` holds `Blazor.Host` (the server that renders the public surface and prerenders the authenticated one), `Blazor.Client` (the WebAssembly client) and `Blazor.Tests`, plus the browser harness and the end-to-end specifications under `blazor/tests/`. Code a native client could reuse lives outside the root, in the platform-neutral projects under `application/`: the account API contracts, the typed clients and the localization resources. The root has its own `global.json`, so every command runs through the developer CLI skills with the `--blazor` target (build, format, lint, test, e2e).

## Implementation

1. Put a page in `blazor/Blazor.Host/Components/Pages/`, by surface:
   - `Public/` for the static server-rendered pages reachable without a session (landing, login, signup, verification, legal) and their form models in `PublicForms.cs`
   - `App/` for authenticated pages; each is a server-rendered page with `[Authorize]` and `[InteractiveSurface]` that hosts one WebAssembly component from `Blazor.Client`
   - `Development/` for fixture pages behind `[DevelopmentOnly]` that the browser harness in `blazor/tests` drives; they return 404 outside Development
   - `Error.razor` and `NotFound.razor` at the folder root; the layout and the public navigation in `Components/Layout/`
2. Put an interactive component in `blazor/Blazor.Client/`, in a folder named after its feature (`Users/`) or, for a component every feature shares, after its concern (`Forms/`, `Components/Lists/`). A feature folder keeps the component, its list source and its client-side services together. WebAssembly fixture components live in `Development/`.
3. Put host-only services in `blazor/Blazor.Host/Shell/` (the host page's headers and policy, brand, manifest, the prerender bootstrap adapter and the page attributes) and `blazor/Blazor.Host/Account/` (token authentication and the credential adapter for the typed clients). Put client services in `blazor/Blazor.Client/Bootstrap/` (bootstrap source, navigator, client registration) or beside the components that use them.
4. Register a service a component injects in both containers: `Blazor.Host/HostApplication.cs` for static rendering and prerendering, `Blazor.Client/Program.cs` for the browser. Where the two sides obtain data differently, define the interface in `Blazor.Client` and give each side an implementation (`IBootstrapSource`: `HostBootstrapSource` in the host, `HttpBootstrapSource` in the client).
5. Put a JavaScript module in `blazor/Blazor.Client/wwwroot/js/` when a component needs it in the browser (`data-list.js`, `unsaved-changes.js`), and in `blazor/Blazor.Host/wwwroot/js/` when only the host page needs it. A component loads its module with `IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/<name>.js")` and hands it a `DotNetObjectReference`; no page adds a script element of its own. A static page has no runtime to import with, so its module is a nonced `<script type="module">` in `App.razor` that acts only on the pages it serves (`blazor/Blazor.Host/wwwroot/js/verification-timer.js`, see the content-security-policy rule); code that must run before first paint on every page is a nonced parser-blocking script in `<head>` instead (`blazor/Blazor.Host/wwwroot/js/theme.js`). Styles are classes in `blazor/Blazor.Host/wwwroot/app.css`.
6. Put a contract in `application/account/Contracts/Features/<Feature>/{Requests,Queries,Domain}/`, keeping the name, namespace and JSON shape of the server command, query or response it mirrors, in the same commit as the code that needs it. Put the account API call in `application/account/Client/<Area>Client.cs` with its path in `AccountApiRoutes.cs`. Neither project may reference a web host, a UI framework, EF Core or MediatR; `PortableAssemblyDependencyTests` in `application/account/Tests` fails when one does.
7. Put a user-visible string in `application/shared-kernel/SharedKernel.Localization/Resources/<Group>Strings.resx` and its `.da-DK.resx` twin, and use the generated `<Group>Strings` class from the component (see the localization rule).
8. Put tests where their subject is:
   - .NET tests in `blazor/Blazor.Tests/`, by area: `Account/` (the running host through `HostFixture`, with a stand-in account API that records what the host sends), `Client/` (client services and components, `Client/Lists/` for the list foundation), `Localization/` and `Shell/`. Names follow `[Method]_[Condition]_[ExpectedResult]` with Arrange, Act and Assert comments, as in the backend api-tests rule
   - Browser harness scripts in `blazor/tests/<name>.mjs` with shared helpers in `blazor/tests/support/`, run through `blazor-harness` (see the blazor-publish skill). They cover what a unit test cannot: the policy, navigation, keyboard, culture and timing in three real browsers
   - End-to-end specifications in `blazor/tests/e2e/<feature>-flows.spec.ts` with adapters in `blazor/tests/e2e/support/`, run with the e2e skill and `--blazor`. They follow the end-to-end-tests rule, import `test` from `@blazor/e2e/authentication` and build every URL through `@blazor/e2e/routes`
9. Rewrite or delete spike code in any file a task touches; a file labelled "Spike code" never survives in the files a task owns.
10. Treat the imported skills under `.agents/skills/` (`author-component`, `use-js-interop`, `support-prerendering`, `coordinate-components`, `plan-ui-change`, `csharp-refactoring`) as generic Blazor and C# mechanics, invoked only for the mechanism they name. Where they differ from these rules, the rules win: modules live in `wwwroot/js/` and load through the import map, never as collocated `.razor.js` or script elements (the static-page case in step 5 aside); data comes through the typed clients and `SessionState`, never `HttpClient`, `[PersistentState]` or a caught `OperationCanceledException`; forms follow the forms-and-validation rule; no token is ever browser-readable; accessible names in both cultures are required, not gold-plating.

## Examples

### Example 1 - An Authenticated Page Hosts a Client Component

```razor
@* ✅ DO: the page in blazor/Blazor.Host/Components/Pages/App/AppDetails.razor hosts a component from Blazor.Client *@
@page "/app/details"
@using Blazor.Host.Shell
@using Microsoft.AspNetCore.Authorization
@attribute [Authorize]
@attribute [InteractiveSurface]

<PageTitle>@AccountStrings.Details</PageTitle>

<AuthenticatedApp Page="details" @rendermode="InteractiveWebAssembly"/>
```

```csharp
// ✅ DO: one interface in the client, one implementation per side (blazor/Blazor.Client/Bootstrap/IBootstrapSource.cs)
public interface IBootstrapSource
{
    Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default);

    Action Apply(BootstrapResponse? bootstrap);
}

// blazor/Blazor.Host/HostApplication.cs registers the host side; Bootstrap/AccountApiRegistration.cs registers the client side
builder.Services.AddScoped<IBootstrapSource, HostBootstrapSource>();
```

### Example 2 - Where the Pieces of One Feature Go

```text
✅ DO: the users list, from contract to test
application/account/Contracts/Features/Users/Requests/UserRequests.cs     GetUsersQuery, ChangeUserRoleCommand
application/account/Client/UsersClient.cs                                 GetUsersAsync, ChangeUserRoleAsync
application/account/Client/AccountApiRoutes.cs                            GetUsers(query), ChangeUserRole(userId)
application/shared-kernel/SharedKernel.Localization/Resources/UsersStrings.resx and UsersStrings.da-DK.resx
blazor/Blazor.Client/Users/UsersListSource.cs                             filter parameters, sort keys, fetch
blazor/Blazor.Client/Users/UsersSurface.razor                             the interactive component
blazor/Blazor.Host/Components/Pages/App/UsersPage.razor                   the page that hosts it
blazor/Blazor.Tests/Client/Lists/UsersListSourceTests.cs                  unit tests
blazor/tests/e2e/user-management-flows.spec.ts                            end-to-end specification

❌ DON'T: declare a request record inside Blazor.Client, call HttpClient from a component, or type a user-visible
   string in a .razor file; each belongs to the project above
```
