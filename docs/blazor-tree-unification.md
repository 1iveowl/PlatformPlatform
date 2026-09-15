# Tree unification at .NET 11 general availability

**This is a plan. None of it has happened.** It was written on 2026-09-13 in session B0b, and hop X2 will carry it out once .NET 11 general availability ships (expected 2026-11-10). Statements about the current tree were verified at commit `d5085c1f1` on 2026-09-13. Anything not verified is marked as an assumption.

## Starting point

Verified at `d5085c1f1`, the tree has three build roots:

| Root | Solution | SDK in `global.json` | Projects |
| -- | -- | -- | -- |
| `application/` | `PlatformPlatform.slnx` | 10.0.301 | 13 `net10.0` projects and 4 `.esproj` shims (`Microsoft.VisualStudio.JavaScript.Sdk/1.0.751930`) |
| `developer-cli/` | `DeveloperCli.slnx` | 10.0.301 | 1 `net10.0` project |
| `blazor/` | `Blazor.slnx` | 11.0.100-rc.1.26425.128, `allowPrerelease` true | 2 `net11.0` projects: `Blazor.Host` and `Blazor.Client` |

Also verified at `d5085c1f1`:

* **The working directory selects the SDK, not the project file's location.** Evaluating `blazor/Blazor.Host/Blazor.Host.csproj` for `NETCoreSdkVersion` gives 10.0.301 from `application/` and 11.0.100-rc.1.26425.128 from `blazor/`.
  * The developer CLI therefore runs `build`, `format` and `lint` for each root from that root's folder.
  * `test`, `e2e` and `check` do not cover `blazor/` yet.
* **CI:** GitHub workflows point `setup-dotnet` at a `global-json-file` in 8 places, 7 for `application/global.json` and 1 for `developer-cli/global.json`. No workflow builds `blazor/`.
* **Container images:** five Dockerfiles (AppGateway, and the account and main Api and Workers) use `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` or `-chiseled-extra`.
* **Packages:**
  * `application/Directory.Packages.props` pins 15 `Microsoft.*` packages at 10.0.9, which ship with the runtime, among them `Microsoft.EntityFrameworkCore` and `Microsoft.AspNetCore.Authentication.JwtBearer`.
  * Two EF Core provider packages are at 10.0.1: `Npgsql.EntityFrameworkCore.PostgreSQL` and `EFCore.NamingConventions`.
  * Three `Microsoft.Extensions.*` packages that version separately from the runtime are at 10.7.0: `Http.Resilience`, `ServiceDiscovery` and `ServiceDiscovery.Yarp`.
  * Aspire packages are at 13.4.6, including `Aspire.Hosting.JavaScript`.
* **AppHost:** it adds projects with the generic `AddProject<T>` and the React build with `AddJavaScriptApp("frontend-build", "../")`.
* **CLI prerequisite:** `Prerequisite.Dotnet` in `developer-cli/Installation/Prerequisite.cs` requires at least 10.0.301.
* **Development container:** its definition lives outside this repository and installs both SDKs (session B0a).

## Target state

* **One SDK:** the general availability release of SDK 11 for every root.
* **Retargeted projects:** the backend, the AppHost, the gateway and the developer CLI target `net11.0`.
* **Blazor inside `application/`:** the Blazor projects move into `application/` and build as part of `PlatformPlatform.slnx`. The `blazor/` root is removed.

Folding the Blazor projects in removes the only reason the sibling root exists, the different SDK pin. It also lets the AppHost add the Blazor host with the same generic `AddProject<T>` as every other project, and gives Blazor one package file shared with the backend contracts it consumes.

The folder the Blazor projects move to is not decided here. Stage C (sessions C1 and C2) settles the layout of the host and the contracts projects, and X2 follows it.

## Order

Each step is one commit, with build, format, lint and test green before the next starts. The end-to-end suite runs after steps 2 and 4. Keeping the SDK change apart from the retarget means that a failure in step 1 is caused by the toolchain, not by the runtime or packages.

### 0. Preconditions

* .NET 11 general availability has shipped, and the owner confirms the hop is due.
* The development container has the general availability SDK installed. The RC stays installed until step 4 is committed, so a revert still builds.
* The Aspire version that will be used supports the `net11.0` runtime, with the evidence recorded (X2 asks for this).
* `Npgsql.EntityFrameworkCore.PostgreSQL` has a release for EF Core 11. **Assumption**, not checked.
* The FluentUI Blazor and Blazor-ApexCharts versions are re-resolved against the general availability SDK and recorded. B0b recorded `Microsoft.FluentUI.AspNetCore.Components` 5.0.0-rc.5-26219.1 and `Blazor-ApexCharts` 7.0.0, both shipping only `net10.0` assets.

### 1. One SDK, targets unchanged

* Set the general availability version in `application/global.json`, `developer-cli/global.json` and `blazor/global.json`, and remove `allowPrerelease` from the Blazor pin.
* Raise the `Prerequisite.Dotnet` minimum to the same version.
* The CI workflows follow automatically, because `setup-dotnet` reads the same `global.json` files.
* Build all three roots with their targets unchanged. SDK 11 builds `net10.0` projects.
* Verify that the `.esproj` shims load under the SDK 11 MSBuild. **Assumption**, not checked.
* Verify that the pinned `jb` tool (2026.1.3) still formats and lints `application/`. B0b verified it lints `blazor/` on SDK 11 RC1.

### 2. Retarget the backend

* Change `net10.0` to `net11.0` in the 13 `application/` projects.
* Move the 15 runtime-aligned `Microsoft.*` pins, the two EF Core provider packages and the Aspire packages to their .NET 11 releases.
* Check whether the three separately versioned `Microsoft.Extensions.*` packages need a new release. Leave packages whose major version only happens to be 10, such as `Mapster` and `coverlet.collector`, as they are.
* Move the five Dockerfiles to the `11.0` image tags.
* Two projects generate OpenAPI documents on build, and the React edition generates TypeScript from them. Rebuild the frontend and review any diff in the generated clients before committing.
* Run the Aspire restart and the end-to-end suite.

### 3. Retarget the developer CLI

* Change `developer-cli/DeveloperCli.csproj` to `net11.0`.
* The CLI rebuilds itself on change, so verify it can still rebuild and run before the commit. A broken CLI cannot run the checks that would catch it.

### 4. Fold `blazor/` into `application/`

* Move `Blazor.Host` and `Blazor.Client` into the folder stage C chose, and add them to `PlatformPlatform.slnx`.
* Merge `blazor/Directory.Packages.props` into `application/Directory.Packages.props`.
* Delete `blazor/global.json`, `Blazor.slnx`, `Blazor.slnx.DotSettings`, `.editorconfig`, `dotnet-tools.json` and `README.md`. The `application/` copies of the last three are identical to the ones removed.
* Remove the `--blazor` target and `Configuration.BlazorFolder` from the developer CLI, and the flag from the build, format and lint skills. The Blazor projects are then built, formatted and linted as backend code.
* Re-point the CI job that stage C (session C6) will have added for `blazor/`, and any Aspire resource added by path in stage B or C, to the new locations.
* Update `docs/BLAZOR.md` where it names `blazor/`.

## What the React edition needs until it is retired

React stays buildable and testable until stage H removes it. Through the unification that requires:

* **Node and the npm workspace:** the Node version in `application/.node-version` (24.14.0 at `d5085c1f1`), the npm workspace, and the `--frontend` targets of `build`, `format` and `lint`, including the missing-translation check.
* **Build shims:** the four `.esproj` shims, loadable by the SDK 11 MSBuild.
* **Aspire resource:** the `frontend-build` JavaScript resource in the AppHost, and an `Aspire.Hosting.JavaScript` release matching the Aspire version chosen in step 0.
* **Generated clients:** OpenAPI documents generated on build and the TypeScript clients generated from them, regenerated and reviewed after step 2.
* **Email templates:** the React Email templates, until stage E moves them to Razor.
* **Gateway routes:** the AppGateway routes that serve the React applications.
* **End-to-end suite:** the Playwright suite, run against React until each flow is re-pointed at Blazor.
* **Upstream frontend commits:** taken at the existing cadence until React is retired, as decided on 2026-09-13.

No step touches `application/*/WebApp`, `application/account/BackOffice` or `application/shared-webapp` sources. B0b verified that the frontend output is unchanged by hashing every file in the frontend `dist/` folders and the first-party backend assemblies before and after the CLI change; X2 will repeat that comparison around step 1.

## Rollback

Each step is a single commit, and reverting it restores the previous state. The RC SDK remains installed until step 4 is committed, so reverting steps 1 to 3 does not depend on the container.
