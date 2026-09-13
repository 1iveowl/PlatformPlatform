# AI usage optimisation

How this repository is set up to be worked on by AI coding sessions cheaply, and how that setup was arrived
at. Current as of 2026-09-13 on branch `experiment/05-ai-usage`.

A session here is a Claude Code run: either one pair-programmer working directly with a developer, or a team
lead delegating to engineer, reviewer, guardian and researcher teammates. Both load this repository's
instructions before doing anything, and both bill for every token they carry. This page describes what that
costs, what was changed to lower it, and what is known to work.

## Where the money goes

Measure before changing anything. `pp claude-usage` reads the transcripts Claude Code writes locally and
reports per session, per model, per tool, and per cache behaviour:

```
pp claude-usage --since <date> --quiet
```

Nine days of real feature work (three pull requests plus environment setup) cost $1,682 at list price across
5,805 API calls. The shape of that number is what matters:

- **Conversation length dominates.** A session starts at 50 to 58k tokens of context and grows. On the long
  sessions most calls ran above 200k tokens, and every call re-reads the entire conversation so far. The
  always-loaded instruction files were about 12% of a typical call; the other 88% was accumulated
  conversation.
- **Teammates cost five times more per call than the main session** in newly written cache: 24,857 tokens
  against 5,030.
- **Tool results stay in context forever.** Build and test output, whole-file reads, and Linear API
  responses that echo an entire issue back accounted for the largest single results in the window.
- **Rules loaded too eagerly.** Touching any JSON file pulled in 27 KB of instructions; any `.tsx` pulled 32 KB.

That points the work at four places: how long a session runs, what it puts in its context, what it loads
before it starts, and how the cache behaves.

## What changed

### The instruction set loads less, and conditionally

`AGENTS.md` is 41 lines. It carries only what a session cannot derive from the code and what no hook already
enforces: the developer CLI commands to use, the Aspire and commit rules, and one emphasised line requiring
every path, name and route to be verified against the codebase. Published guidance puts the ceiling at 200
lines and reports that instruction following degrades uniformly as instruction count grows, so an unnecessary
line does not merely cost tokens, it weakens the lines that matter.

Everything else moved down a tier:

- `.claude/rules/*.md` carry `paths:` frontmatter and load only when a session touches a matching file. Every
  rule is now scoped; the broad `**/*.json` and `.editorconfig` patterns were narrowed, and the 20 KB
  frontend rule was split to 5 KB so a component edit does not pull in the build system and email templates.
- Procedures live in skills, which load on invocation. A `codebase-overview` skill replaced a 23 KB
  architecture map that every session used to read at startup. Directory layouts and dependency lists are
  exactly what a session can discover with `ls`, `grep` and the language servers; what it cannot discover is
  the cross-cutting patterns to copy and the known traps, so only those survive in the skill.
- MCP servers default to Aspire and Linear. Azure, shadcn and Stripe are enabled per task in
  `settings.local.json`. Tool schemas are deferred, so a connected server costs its name rather than its
  full definition.

### Sessions stay short and return receipts

The dominant cost was conversation length, so the agent definitions now treat a session as disposable:

- One task per session. At 200k tokens of context an agent finishes its step, writes a handoff, and stops
  rather than continuing into an expensive tail.
- Every agent's final message is a receipt of about 1,500 tokens: status, files changed, commands run with
  results, blockers. Full logs stay on disk. A caller reads the receipt, not a transcript.
- Spawn prompts name the Linear issue and add only what the issue does not already say, because everything
  in a spawn prompt sits in that teammate's context from its first turn.
- Linear writes send the smallest field set and never re-fetch what was just written.

### The developer CLI answers in summaries

`build`, `test`, `format` and `lint` print failures, build-breaking warnings, and a one-line total. `--verbose`
restores the full output. A passing test run that used to add thousands of tokens to a session now adds a
line. This is the cheapest kind of saving available: the filtering happens outside the model.

### Caching is protected

Prompt caching charges a tenth of the input price to re-read a stable prefix and full price to rebuild one,
so the enemy is churn rather than size. Model and effort are pinned in every agent definition, because
changing either mid-session rebuilds the entire cache. Teammate cache lifetime is set to one hour, since a
guardian or architect routinely idles longer than the five-minute default between task sets.

An investigation into the teammate cache-write excess found that most of it came from a Claude Code defect:
resuming a teammate re-emitted the skill listing into the middle of the prompt, invalidating everything after
it. That is fixed in current Claude Code versions. The part that remains ours is cache expiry during
long-running commands such as end-to-end test suites, which the one-hour lifetime covers. The
`Cache write attribution` section of `pp claude-usage` splits every cache write into cold start, re-written
prefix and genuinely new material, so a future regression of this kind is visible rather than mysterious.

### Specifications are clarified before code is written

Rework is more expensive than tokens. Borrowing the discipline of spec-driven development and implementing it
over Linear rather than as files:

- A versioned constitution (`.claude/reference/constitution.md`) states the non-negotiables: verify before
  asserting, no test weakened to save effort, stop and ask rather than guess.
- `create-prd` now runs a clarify pass before tasks are written: at most five questions, one at a time,
  drawn from a fixed taxonomy (scope, data model, flows, non-functional needs, integrations, edge cases,
  constraints, terminology, completion signals). Anything still unspecified is labelled and blocks the task
  it affects instead of being guessed at.
- `analyze-feature` is a read-only consistency check over a Linear project before work starts: duplicated
  requirements, tasks without acceptance criteria, criteria that trace to nothing, terminology drift, and
  conflicts with the constitution.

## What is established, and what is not

Established:

- The always-loaded instruction set is smaller and every rule is conditional.
- Behaviour did not regress. Five prompts, each designed to tempt one rule (a direct `dotnet` call, a `cd`, an
  unrequested commit, an attribution trailer, answering from memory), were run in fresh sessions before and
  after. The result went from four of five to five of five; the rule that had failed now passes because the
  rewritten instruction addresses it directly.
- Cache behaviour is attributable per call, per agent and per model.

Not established:

- **The per-session saving on real feature work.** A four-task comparison between the branch and `main`
  showed the expected reduction on a tool-heavy task and none on read-heavy ones, at one run per cell, which
  is too small to conclude anything. The real test is a full feature built through this setup and diffed
  against the recorded baseline with `pp claude-usage --diff`. That run is still outstanding and decides
  whether this branch merges.
- **Cheaper models for research.** Sonnet was trialled for the researcher role and reverted: asked which
  token claims two identity providers actually emit, it found one of three facts that Opus found three of.
  Both roles stayed on Opus, and both definitions gained a requirement that a claimed divergence between a
  provider and its documentation be confirmed against a real call rather than inferred. Two such divergences
  had already reached production-shaped code undetected, each caught only by a live run.

## Further reading

`.claude/reference/ai-usage/` holds the detail: the original plan and its reasoning, the measured baseline,
the behaviour-check transcripts and scoring, the branch-versus-main comparison, and a source-cited research
record in which every external claim carries its URL.
