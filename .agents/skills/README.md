# Imported .NET skills

Source: https://github.com/dotnet/skills, revision `5b4d76e8b45dc195bc1b5dd3a860c78e98a47954`, MIT (see
`DOTNET-SKILLS-LICENSE.txt`). Skill directories and their supporting files are copied unchanged; each has a relative
symlink of the same name under `.claude/skills/`, which is where the assistant discovers them.

Kept, as generic Blazor and C# mechanics that the rules under `.claude/rules/blazor/` do not cover: `author-component`,
`use-js-interop`, `support-prerendering`, `coordinate-components`, `plan-ui-change`, `csharp-refactoring`. The other
ten skills of the three collections (`dotnet`, `dotnet-aspnetcore`, `dotnet-blazor`) were removed on 2026-09-16
because they either contradict this repository's rules (data access through `HttpClient`, `ValidationSummary` forms,
`[PersistentState]`, controllers, Identity pages) or do not apply to it (project scaffolding, hosting-model
conversion, SDK installation, a third-party component library, OpenTelemetry setup).

Precedence: `AGENTS.md`, the constitution and the path-scoped rules under `.claude/rules/` win over anything in these
skills, including their command examples; build, test, format, lint, end-to-end and Aspire operations go through the
developer CLI skills. The `structure` rule for the Blazor root states the specific points where the rules differ.

To update, review a new upstream revision, replace only these six directories, keep the supporting files, and verify
the symlinks and that no skill name collides with one under `.claude/skills/`.
