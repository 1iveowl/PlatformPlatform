Imported into the fork on 2026-09-12.

# AI usage and token spend: experiment plan

Status: draft, 2026-09-12. Written for the fork owner; nothing here has been executed. Decisions taken while
drafting: the experiment lives on a branch in the fork, cheaper models are trialled for non-code roles only, and
spec-kit is adopted as a discipline implemented over Linear rather than as its file store.

## 1. Context

The plan to send PR 1 upstream is dropped and PlatformPlatform is now a permanent fork. That dissolves the
"PlatformPlatform/ is pristine" rule that shaped this workspace: the duplicate CLAUDE.md, the `../notes/`
startup reads, the external document graph and the additive-only design constraint all existed to keep the
clone mergeable upstream. With that gone, the AI harness can live in the fork itself and be optimised freely.

The first thing to optimise is how much the harness costs to run. A Blazor migration is planned separately and
is out of scope here; the harness should not assume React lives forever, so nothing in this plan bakes
framework specifics into the always-loaded instruction set.

## 2. Baseline, measured 2026-09-12

The totals below are superseded. Use `baseline-2026-09-12.md` next to this file, or regenerate with
`pp claude-usage`; the per-call ratios and the conclusions drawn here stand. This section is kept as the
record of what the plan was based on.

Source: the 14 main-session and 51 subagent transcripts under `~/.claude/projects/` for this container,
2026-09-03 to 2026-09-12, covering PR 1 to PR 3 plus environment work. The analysis scripts are in the session
scratchpad and will be checked in as step 4.1.

| | Calls | Cache read | Cache write | Output | Median context per call |
|---|---|---|---|---|---|
| Main sessions | 5,632 | 2.29 B | 37.5 M | 7.9 M | 393 k |
| Subagents and teammates | 7,627 | 1.26 B | 232 M | 0.38 M | 172 k |

At list price (Opus 5: read $0.50, write $6.25, output $25 per M; Fable 5.1: $0.25, $12.50, $50) this is about
$4,000 for nine days: roughly $1,300 main-session Opus, $340 main-session Fable, and $2,400 for subagents, of
which about $1,800 is cache writes alone. Any long-context premium above 200k tokens is not included and is
unverified; check the pricing page for the `[1m]` model before treating these as exact.

What the numbers say:

1. **Conversation length is the cost, not the instruction files.** A fresh session starts at 50 to 58k tokens.
   79% of main-session calls run above 200k and those calls carry 94% of all input tokens. The always-loaded
   instructions are about 12% of a median call.
2. **Subagent cache writes are anomalous.** 27k new tokens per call versus 5.7k in main sessions, and 85% of
   them occur with under 60 seconds between calls, so TTL expiry is not the cause. Either engineer turns ingest
   very large tool results (build, test and lint output, whole-file reads) or something churns the cached prefix.
   Step 4.2 settles which.
3. **Linear payloads land in the lead's context.** `save_issue` alone echoed 1.2 MB back over 154 calls;
   `get_project` and `get_issue` another 0.44 MB. Hooks cannot filter MCP output.
4. **The unconditional load of a fresh session is ~6.5k tokens**: AGENTS.md (3.6 KB, symlinked as
   `.claude/CLAUDE.md`), the agent definition, two unscoped `feature-flags.md` rules (10 KB), 21 skill
   descriptions and 12 agent descriptions. `ENABLE_TOOL_SEARCH=true` already defers MCP schemas.
5. **Path-scoped rules are broad.** `**/*.json` pulls 27 KB from both core rules; any `.tsx` pulls 32 KB; a
   session touching backend, frontend and E2E can load the full 149 KB rule set (~37k tokens).
6. **The workspace startup path is ~62 KB** (CLAUDE.md 16 KB, current.md 16 KB, codemap.md 23 KB) and
   `decisions.md` (38 KB) has no status banners, so a reader cannot skip superseded entries.
7. **Nothing measures token spend today**, and `ultra-review` instructs agents not to save tokens.

## 3. What the sources say, in one screen

Primary sources are Anthropic's docs and engineering posts; secondary are HumanLayer and Aider. Full briefing
with URLs is kept as the research record for this plan (see section 8).

- CLAUDE.md: target under 200 lines; include commands, conventions that differ from defaults, gotchas; exclude
  anything derivable from code (`/doctor` actively proposes cutting directory layouts and architecture
  overviews); emphasis on many lines makes none stand out; test an instruction by observing behaviour in a
  fresh session. Instruction following degrades uniformly as instruction count grows, not only for the newest.
- AGENTS.md: open standard, no size guidance; Claude Code reads CLAUDE.md, so use `@AGENTS.md` or a symlink.
- Progressive disclosure: always-needed facts in CLAUDE.md, tree-scoped facts in `.claude/rules/` with
  `paths:`, procedures in skills. `@` imports do not save context; they load at launch.
- Caching: exact prefix match; the 5-minute TTL applies to subagents even on a subscription unless
  `subagentPromptCacheTtl` is set; model or effort switches mid-session rebuild the whole cache; deferred MCP
  tools keep the prefix stable. Cache read is 0.1x input, so a large stable prefix is cheap and churn is expensive.
- Subagents: return a 1,000 to 2,000 token summary; agent teams cost about 7x a single session; keep spawn
  prompts short because they enter the teammate's context from turn one; `Explore` and `Plan` skip CLAUDE.md
  by design.
- MCP: prefer CLIs (`gh`, `az`) over MCP servers; disable servers a session does not need; server instruction
  blocks are loaded prose even when schemas are deferred.
- Hooks: PreToolUse and PostToolUse stdout never enters context, so guards cost nothing; hooks are also the
  right tool for filtering large outputs down to failures, and for enforcing what CLAUDE.md can only advise.
- Codemaps: Aider's generated repo map defaults to a 1,000-token budget; Anthropic favours lightweight
  references loaded on demand over pre-loaded overviews; the sanctioned alternative is LSP plus a
  codebase-overview skill.
- Measuring: `/usage` gives per-session cost, cache hit share and, on paid plans, a breakdown by skill,
  subagent and MCP server; `/context` shows what loaded; OpenTelemetry (`claude_code.token.usage` with
  `agent.name`, `skill.name`, `mcp_server.name`) is the only per-role attribution; `ccusage` reads local JSONL.
- spec-kit v1.0.6: constitution, specify, clarify (at most five questions, one at a time, nine-category
  taxonomy), plan, tasks (`T001 [P] [US1]`), checklist, analyze (read-only, constitution conflicts are
  CRITICAL), implement. It is file-based, its issue export is GitHub-only, and it makes no token claims.

## 4. Workstreams

Ordered by expected saving and by dependency. Each step names its success check.

### 4.1 Instrument before changing anything

- Check in the transcript analysis as `scripts/claude-usage-report.py` in this workspace: per-session and
  per-subagent totals, per-call context distribution, cache-write by inter-call gap, tool result bytes by
  tool, top single results. Output a Markdown table so a baseline and a follow-up can be diffed.
- Record the section 2 baseline as `notes/results/ai-usage-baseline-2026-09-12.md` (the ownership table
  already reserves `notes/results/` for immutable outcome notes).
- Enable `/usage` cache statistics in every session and note the likely-cause line when hits fall.
- Optional, only if per-role attribution is needed after the first comparison: OpenTelemetry export from the
  dev container to a local collector, keyed on `agent.name`.
- Success: a second run of the script on the next task set produces a comparable table with no manual steps.

### 4.2 Explain the subagent cache-write anomaly

- Extend the script to attribute cache-write tokens per call to the tool result that preceded it, and to
  flag calls where cache read drops while the conversation grows (prefix churn) versus calls where cache read
  continues and cache write equals the new tool result (large ingestion).
- Cross-check with `/usage` on one live engineer teammate.
- Success: one sentence naming the cause, and a fix in 4.4 or 4.5 depending on which it is.

### 4.3 Session-length discipline (largest lever)

Applies to the team lead and pair-programmer definitions in the fork.

- One task set per lead session; the lead hands off through Linear, not through its own transcript. Session
  recovery already exists in `team-lead.md`; make it the normal path rather than the exception.
- Compaction policy: compact or restart at the hand-off point in `.claude/reference/constitution.md` section 6
  (200k until 2026-09-17, then a 300k trial), never run to the 1M limit. Prefer
  `/rewind` to a cached prefix over compaction when the tail is what needs dropping.
- Receipts, not transcripts: every agent definition gets a "return" section capping the final message at about
  1,500 tokens (status, commit, check results, blockers, path to full logs). Spawn prompts point at the Linear
  issue and say nothing the issue already says.
- Linear payloads: agents write issues with the smallest field set and never re-fetch what they just wrote;
  the lead reads a project once per task set. Evaluate a thin `pp linear` command over the GraphQL API that
  returns ids and status only, replacing the MCP for writes; the MCP stays for reads that need bodies.
- Large tool output: a PreToolUse hook (or the developer CLI itself) trims build, test, format and lint
  output to failures and totals. The CLI is the better home since it already owns those commands.
- Success: median context per call under 200k on the next task set; Linear tool result bytes down by an
  order of magnitude.

### 4.4 Caching and model pinning

- Set `subagentPromptCacheTtl` to one hour in the fork's `.claude/settings.json`; the guardian, architect and
  researcher idle between task sets.
- Pin `model:` and `effort:` in every agent definition (pair-programmer currently has neither) and forbid
  mid-session `/model` and `/effort` switches in the definitions; both rebuild the cache.
- Success: cache hit share reported by `/usage` above 90% for teammates.

### 4.5 Slim the always-loaded set (CLAUDE.md and AGENTS.md practice)

Done on the experiment branch in the fork.

- Rewrite the fork's `AGENTS.md` as the single root instruction file, under 200 lines, merging only what the
  workspace CLAUDE.md still needs to say now that the clone is the home: commands, conventions that differ
  from defaults, gotchas, the attribution rule as one line pointing at the hook. Keep `.claude/CLAUDE.md` as a
  symlink; verify with `/context` that it is not loaded twice.
- Remove prose that restates what hooks enforce (bash guard, attribution guard, excuse checks) beyond one
  line each, and remove ALL-CAPS emphasis except on the one instruction most often skipped.
- Add `paths:` to `backend/feature-flags.md` and `frontend/feature-flags.md`; narrow `**/*.json` and
  `.editorconfig` in the two core rules; split `frontend/frontend.md` (20 KB) so a `.tsx` edit loads only what
  applies. Keep the fork's own `ai-rules.md` guidance, which already says globs must be specific.
- Trim skill descriptions that exceed about 150 characters (`upgrade-packages` is 573) and review
  `team-lead.md` (27 KB) for content that belongs in a skill invoked at task-set start instead.
- Delete the `ultra-review` lines that say not to save tokens; replace with "depth over brevity in findings,
  receipts for everything else".
- Success: `/context` on a fresh pair-programmer session under 5k tokens of project instructions; behaviour
  checks in fresh sessions for the five rules most often broken (direct dotnet calls, `cd`, proactive commits,
  attribution, unverified paths).

### 4.6 Codemap: right idea, wrong tier

Verdict on "do we have the right codemap": the content is useful, the placement is not. It is a 23 KB
mandatory startup read that duplicates what `ls`, LSP and the path-scoped rules already provide, and it
carries an inventory of `.claude/` that rots on every rebase.

- Move into the fork as a `codebase-overview` skill: sections 9 (patterns to copy) and 10 (gotchas) largely
  as they are, plus a short routing table of which folder owns what. Target under 6 KB. Delete section 7
  entirely and sections 1 to 2 down to the routing table.
- Keep `notes/graphs/project-dependencies.md` as the generated map and have the skill reference it; consider
  extending `gen-project-graph.sh` to emit a symbol-level map with a token budget, Aider style, only if the
  overview skill proves insufficient.
- Fold `glossary.md` into the skill's references and drop `architecture.md` prose in favour of the diagrams.
- Drop the "start every session with codemap.md" instruction from every startup path; the skill description
  is what triggers it.
- Success: no session reads the codemap unless it invokes the skill; the skill fires on the tasks that need
  orientation (verify with three fresh sessions).

### 4.7 Model policy for non-code roles

- `researcher.md` and `regression-tester.md` to Sonnet 5 at high effort. Guardian stays Opus/medium because it
  runs final validation and commits. The team lead stays on Opus for this round; revisit once 4.3 shows how
  many of its turns are pure coordination.
- Keep the Fable escalation for architect, responsible reviewer and security research on identity, payment,
  privacy and irreversible-data boundaries. Reviewer independence from the implementer's model is kept.
- Success: research and regression output judged equivalent on one task set; cost per role from the report.

### 4.8 MCP scope

- Move `linear-server` from user scope into the fork's `.mcp.json` and `enabledMcpjsonServers` so it is a
  project decision.
- Default `enabledMcpjsonServers` to `aspire` and `linear-server`; enable `azure-*`, `shadcn` and the Stripe
  plugin per task through `settings.local.json`. The two Azure servers ship long instruction blocks that load
  as prose.
- Figma is a claude.ai connector; disable it for this project unless a task needs it.
- Success: `/context` shows only the servers the task set needs.

### 4.9 Spec-driven discipline over Linear

Implemented as skills in the fork, extending upstream's `create-prd`. No `.specify/` tree.

| spec-kit | Fork over Linear |
|---|---|
| constitution.md | A versioned Linear document "Constitution" plus the same text in `.claude/reference/constitution.md`; carries the tense and evidence rules from this workspace, the andon-cord rule, and the token discipline of 4.3 |
| specify | Project description written by `create-prd` (already the case) with `[NEEDS CLARIFICATION]` markers allowed |
| clarify | New `clarify` step in `create-prd`: at most five questions, one at a time, from the nine-category taxonomy; answers appended as a dated Clarifications section; each open marker becomes a `needs-clarification` label and a blocking relation |
| plan, research, data-model | Linear documents attached to the project, one per artifact |
| tasks | Milestones for phases (Setup, Foundational, per user story, Polish); issues titled `T001: ...`; `parallel-ok` label for `[P]` |
| checklist | One "Spec quality gate" issue with the checklist |
| analyze | New read-only `analyze-feature` skill: fetches constitution, project description, documents and issues, reports inconsistencies, constitution conflicts as CRITICAL, writes nothing |
| implement | The existing team-lead flow |

- Success: the slice 8 comparison feature runs through this flow end to end.

### 4.10 Workspace consolidation

- Shrink the workspace `CLAUDE.md` to what a workspace-rooted session still needs: the layout, the attribution
  guard, the pointer to the fork's AGENTS.md. Remove the pristine-clone section, the planned-PRs section and
  the duplicated model matrix.
- Add status banners to superseded entries in `decisions.md`; register this plan and the results note in
  `document-graph.json`; retire `pr-plan.md` section 1 constraints that only served upstream mergeability and
  record that as a decision.
- Re-evaluate the four upstream-only Linear items (EP-10, EP-12, EP-20, EP-21) as fork fixes or closures.
- Success: `python3 scripts/check_document_graph.py` clean; workspace startup path under 20 KB.

## 5. Order of execution

1. 4.1 and 4.2 first, on main, since they change no behaviour and settle what 4.3 to 4.5 must fix.
2. Branch `ai-harness` in the fork. 4.4, 4.5, 4.6, 4.8 together: they are one coherent edit of `.claude/`.
3. 4.3 and 4.7: agent definition changes.
4. 4.9: skills, then run PR 4 through them as the comparison task set.
5. 4.10 last, once the fork's AGENTS.md is the source of truth.

## 6. Verification

- Before and after tables from the usage report: the PR 3 task sets are the before; the slice 8 comparison
  feature is the after. PR 4 (`mitid-login`) was found Completed in Linear on 2026-09-12, so the comparison
  is a Backlog feature of similar size to one PR 3 task set, `entra-directory-policy` being the candidate. Compare cost per task set, median context per call, subagent cache-write per call,
  Linear result bytes, and review findings per task set so rigour is visible next to cost.
- Fresh-session behaviour checks for the five most-broken rules after 4.5.
- `/context` on a fresh session for each of pair-programmer, team-lead, backend and frontend agents.
- Attribution scan and the document-graph checker stay as the push gates.

## 7. Risks and open questions

- Long-context premium pricing for the `[1m]` model is unverified; if it applies, the 4.3 saving is larger
  than estimated and the baseline cost is understated.
- The subagent cache-write cause is unknown until 4.2; the 4.4 and 4.5 fixes assume different causes.
- Upstream rebases will conflict with harness edits. Accepted: the fork is permanent, and the eleven
  skip-worktree marks should be dropped once the branch owns the files.
- Sonnet for the researcher may miss provider divergences of the kind EP-25 records; the A/B in 4.7 exists to
  catch that, and the Fable escalation for security research stays.
- The Blazor migration will change the frontend rules wholesale; 4.5 keeps framework content in path-scoped
  rules so the always-loaded set survives that change.

## 8. Execution: slices, sessions, models

Decisions taken 2026-09-12: implementation happens in the private fork on branch `experiment/05-ai-usage`
(following the existing `experiment/NN-name` convention; `origin/main` was 1cfde11d3 when checked), the
plan, baseline and research record move into the fork under `.claude/reference/ai-usage/` so others can read
them without this workspace, Linear holds one project named after the branch with one issue per slice, and the
pair-programmer is the primary target. Team-lead gains are a by-product, verified once at the end.

Working rules for every slice, which are themselves the discipline under test:

- One slice per session, fresh session, rooted at the fork on the branch, started from the Linear issue.
- Pick model and effort at the start; never switch mid-session (each switch rebuilds the cache).
- End with a receipt on the Linear issue: what changed, the `/usage` line (cost, cache hit share), and the
  fresh-session checks run. Full logs stay local.
- Effort `high` is the default. `xhigh` only where the table says so. `medium` for mechanical edits.

| Slice | Content | Model, effort | Why this tier |
|---|---|---|---|
| 0 | Branch; narrow the attribution scan and push guard to authorship (identities, trailers, footers, commit and PR text) so documentation may name the tooling; import plan, baseline and research to `.claude/reference/ai-usage/`; create the Linear project and issues | Sonnet 5, medium | Mechanical, fully specified; the regex change is reviewed by a human |
| 1 | `pp claude-usage` in the developer CLI: reads `~/.claude/projects` transcripts, prints the section 2 tables, `--since`, `--diff <baseline.json>`; record the baseline through it | Opus 5, high | Code under `developer-cli` rules; the Python scripts are the specification |
| 2 | Explain the subagent cache-write anomaly (4.2) using slice 1 output plus `/usage` on one live teammate; one-paragraph finding on the issue | Opus 5, high; escalate to Fable 5.1 if unresolved after one session | Investigation, no code; judgement matters more than volume |
| 3a | Rewrite the fork's `AGENTS.md` under 200 lines; one line per hook-enforced rule; symlink check with `/context` | Fable 5.1, xhigh | Highest-leverage file in the harness; errors are silent and paid every session |
| 3b | `paths:` on the two unscoped rules, narrow `**/*.json` and `.editorconfig`, split `frontend/frontend.md`, trim skill descriptions, remove the "do not save tokens" lines, `subagentPromptCacheTtl`, model and effort pinned in every agent definition, MCP scope per project | Opus 5, high | Many small edits with a clear rule each; needs care, not deep design |
| 3c | Fresh-session behaviour checks for the five most-broken rules, before and after 3a and 3b | Opus 5, high | Must run on the production model, otherwise the check measures the wrong thing |
| 4 | `codebase-overview` skill from codemap sections 9 and 10 plus a routing table; delete the startup-read instruction; three fresh-session trigger checks | Opus 5, high | Curation of existing text with a size budget |
| 5 | Pair-programmer discipline: one task per session, compact at 200k, receipts, small Linear payloads; developer CLI trims build, test, format and lint output to failures and totals; the same receipt section in the other agent definitions | Opus 5, high | Mixed prompt and CLI code; the CLI part is the larger share |
| 6 | Constitution (Linear document plus `.claude/reference/constitution.md`), `clarify` step in `create-prd`, `analyze-feature` read-only skill, milestone and `T001` conventions | Fable 5.1, high for the constitution and skill design; Opus 5, high for wiring | Prompt design with long-lived consequences; the wiring is ordinary |
| 7 | Researcher and regression tester to Sonnet 5; Linear payload path (thin `pp linear` write command) if slice 2 or the baseline shows it is worth it | Opus 5, high | Small code; the decision is already taken |
| 8 | Comparison run: a Backlog feature of similar size to one PR 3 task set (candidate `entra-directory-policy`) as a pair-programmer task set with the new harness; `pp claude-usage --diff` against the baseline; one team-lead task set only if the pair-programmer numbers hold | Per policy: Opus 5, high for implementation, Fable 5.1, xhigh for the final review | This is the measurement, so it runs at the production tier |
| 9 | Workspace consolidation (4.10): shrink `CLAUDE.md`, banners in `decisions.md`, retire upstream-only constraints, update the document graph | Sonnet 5, high | Documentation edits with a checker as the gate |

Slices 0 to 2 run on `main` and the branch without behaviour changes; 3a to 7 are the branch proper and should
merge as one squashed commit per slice; 8 decides whether the branch merges to `main`; 9 follows the merge.

What goes into Linear versus the repo: the project description carries the baseline numbers from section 2,
the working rules above, and the success checks per slice, restated in full. Each issue carries its slice row
and success check in its own words plus the repo path of the file it changes. The full plan and the research
record live in the repo at `.claude/reference/ai-usage/` and are linked by repo path, which is inside the
clone and therefore allowed by the self-containment rule.

## 9. Research record

The full source-cited briefing (spec-kit phases and templates, Anthropic guidance on CLAUDE.md, caching,
MCP, subagents, models and pricing, codemaps, measurement, hooks, plus practitioner sources) was produced on
2026-09-12 and should be saved as `notes/reference/ai-usage-research-2026-09-12.md` in step 4.1 so the
claims in section 3 keep their evidence.
