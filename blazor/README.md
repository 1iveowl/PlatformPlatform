# Blazor build root

Spike code from stage B of the Blazor edition. Stage C productionises or replaces it.

This folder is a build root of its own, beside `application/` and `developer-cli/`. Its `global.json` pins the .NET 11 SDK with prereleases allowed, while `application/global.json` stays on SDK 10.0.301 so the React edition keeps building unchanged.

The SDK is resolved from the `global.json` nearest the current directory, not nearest the project file. Commands for this root therefore run with `blazor/` as the working directory. The developer CLI does this for `build`, `format` and `lint` through the `--blazor` target, which is also part of the default when no target is given.

* `Blazor.Host`: the server host of the Blazor Web App.
* `Blazor.Client`: the WebAssembly client, where interactive components will live.

The plan for moving the whole tree onto one SDK at .NET 11 general availability is in [docs/blazor-tree-unification.md](../docs/blazor-tree-unification.md).
