# Slice 6a: design of the clarify step, the analyze-feature skill and the task conventions

Written 2026-09-12 against `.claude/skills/create-prd/SKILL.md` at 1dca1505b. Slice 6b implements this text in
`create-prd` and in a new `.claude/skills/analyze-feature/SKILL.md`; it changes nothing described here without
recording the change in this file. Field and tool names below were checked against the Linear MCP schema on
2026-09-12: `save_issue` takes `addLabels`, `blocks`, `blockedBy` and `milestone`; `save_milestone` takes
`project` and `name`; `save_issue_label` creates a label; `list_documents`, `get_document`, `get_project`,
`list_issues`, `get_issue` and `list_comments` are the read side.

## 1. Clarify step in `create-prd`

### Where it goes

Step 5 today drafts the PRD and asks for approval in one go. Split it:

- Step 5: draft the PRD as today, with one new drafting rule (below).
- Step 5a, new, "Clarify": the procedure in this section. Runs before the PRD is shown for approval so the
  user approves the clarified text.
- Step 5b: show the PRD and ask for approval, unchanged from the current tail of Step 5.
- Step 6: create the project and issues as today, plus the marker handling in "Remaining markers" below.

### Drafting rule for Step 5

An unspecified point that would change a task's scope, a business rule or an acceptance criterion is never
guessed. It is written inline as `[NEEDS CLARIFICATION: <one-line question>]` at the place in the text where
the answer belongs. A point that only needs a sensible default gets the default and the words "assumed:" in
front of it; it is not a marker. Markers are the only input to the clarify step, so the drafting rule is what
keeps the step short.

### Scan

After drafting, scan the description and every task paragraph against these nine categories, in this order:

1. Functional scope: what is in and what is out.
2. Data model: entities, fields, identity, uniqueness, retention.
3. UX flow: entry points, states, errors, empty states.
4. Non-functional: performance, limits, availability, localisation.
5. Integration: other self-contained systems, external providers, events.
6. Edge cases: concurrency, partial failure, re-entry, deletion.
7. Constraints: permissions, tenancy, feature flags, compliance.
8. Terminology: names that must match the glossary or the code.
9. Completion signals: how the user knows the feature is done and how it is tested.

For each category record Clear, Partial or Missing in a nine-row table kept in the session, not in the PRD.
Every marker maps to one category. Rank the candidate questions: a question that changes the task split or an
acceptance criterion outranks one that changes a business rule, which outranks wording. Keep the top five.
If no category is Partial or Missing and no marker exists, say "No clarifications needed" and go to Step 5b.

### Ask

Ask the questions one at a time, one `AskUserQuestion` call per question, never batched. Each call has:

- `header`: the category name.
- `question`: the marker text, rewritten so it can be answered by choosing an option or by a phrase of five
  words or fewer. The question text ends with "(or type up to five words)".
- `options`: two to four concrete answers with a one-line consequence each; the option the research
  suggests goes first and is marked "(recommended)". The free-text option is the fifth-word-or-fewer phrase.

Stop after five questions, or earlier when the user answers "stop", "skip the rest" or "proceed". Never ask
a sixth question in the same session, even if markers remain.

### Record

After each answer, before asking the next question:

- Replace the marker in the draft with the resolved text, in the sentence where it sat.
- Append one line under `## Clarifications` / `### 2026-09-12` (today's date) in the form
  `- <Category>: Q: <question> A: <answer>`. The section is the last section of the description, after the
  core changes and before "Tasks overview", so it is part of the text that Step 6 stores as the project
  description. A second clarify session on the same feature adds a new dated sub-heading rather than editing
  the first.

### Remaining markers

Markers that were not resolved (beyond the five, or skipped) stay in the text unchanged. In Step 6, after
the issues are created:

- Every issue whose description still contains a marker gets `addLabels: ["needs-clarification"]`.
- One issue titled `Open clarifications` (no `T` number, so it is never picked as a task) is created in the
  project, in [Planned], with a bullet per marker: the category, the question and the issue it affects. Its
  `blocks` list holds every labelled issue. When no marker remains the issue is not created.
- Markers in the description itself but in no task are listed in the same issue and block nothing.

The label `needs-clarification` is created on the PlatformPlatform team by 6b if it does not exist,
description "Carries an unresolved [NEEDS CLARIFICATION] marker; blocked by the Open clarifications issue".

## 2. The `analyze-feature` skill

### Frontmatter

```
name: analyze-feature
description: Read-only consistency report for a Linear project: constitution conflicts, duplicated requirements, missing or untraceable acceptance criteria, terminology drift. Use after create-prd and before the first task starts.
allowed-tools: Read, Grep, Glob, mcp__linear-server__get_project, mcp__linear-server__list_documents, mcp__linear-server__get_document, mcp__linear-server__list_issues, mcp__linear-server__get_issue, mcp__linear-server__list_comments
```

The tool list is the enforcement: no `save_*` tool, no `Write`, no `Edit`, no `Bash`. The skill text repeats
the rule in one line: "This skill writes nothing to Linear or disk; the report is its final message. If asked
to fix a finding, refuse and name the skill or the manual edit that does it."

### Input

One argument: a project name, ID or slug. Without an argument the skill asks for one and stops.

### Procedure

1. Constitution: read `.claude/reference/constitution.md`; also fetch the Linear document "Constitution" on
   the team (`list_documents` with `query: "Constitution"`, then `get_document`). Compare the version lines;
   a mismatch is a HIGH finding on its own. The repository copy is the one used for the checks.
2. Project: `get_project` with `includeMilestones` and `includeResources`; `get_document` for each attached
   document; `list_issues` with `project` and `fields` limited to id, title, description, labels, status,
   projectMilestone. Use `get_issue` with `includeRelations` only for issues labelled `needs-clarification`.
   Read each item once.
3. Inventory, kept in the session:
   - Requirements: every sentence in the description and documents containing must, never, only, always, will
     or shall, numbered R1, R2, ...
   - Acceptance criteria: per issue, the bullets under a heading containing "acceptance" or, failing that,
     the bullets after the first paragraph; numbered `<issue>-AC1`, ...
   - Terms: nouns that name a domain concept, checked against
     `.claude/skills/codebase-overview/references/glossary.md`.
4. Checks, each producing findings with a stable ID prefix:
   - D (duplicate): the same requirement stated in two places with different wording, or the same
     acceptance criterion in two issues. MEDIUM; HIGH when the two versions disagree.
   - M (missing): an issue with no acceptance criteria or with criteria that cannot be observed (no
     input, no expected result). HIGH.
   - T (traceability): an acceptance criterion that no requirement explains, or a requirement that no
     issue covers. MEDIUM.
   - N (naming): two names for one concept, a name absent from the glossary, an issue title not in the
     `T001: ...` form or not in sentence case. LOW; MEDIUM when the two names both appear in issues.
   - C (constitution): a conflict with any principle. Always CRITICAL. Examples: past tense for work not
     yet built; a code claim with no "Verified at"; a task that tells the engineer to skip or weaken a test
     or to drop review; a task that spans more than one commit or session; an item that depends on a file
     outside the repository; a guessed answer where a marker belongs; a mention of a tool vendor.
   - S (structure): a marker without the `needs-clarification` label or without a blocking relation from
     "Open clarifications"; a gap or duplicate in `T` numbers; a `parallel-ok` task that names a file another
     open task in the same milestone names; a multi-story feature without milestones. MEDIUM.
5. Report, as the final message and nothing else, in this fixed shape:

```
# Analyze: <project name>

Constitution <version>, project read at <ISO timestamp>. <n> issues, <n> documents, <n> requirements,
<n> acceptance criteria.

| ID | Severity | Where | Finding | Fix |
| --- | --- | --- | --- | --- |
| C1 | CRITICAL | EP-70 description, paragraph 2 | ... | ... |

Not shown: <n> LOW findings (<IDs>).

Recommendation: Proceed. | Fix C1, M2 and T4 before the first task starts.
```

Severity order in the table: CRITICAL, HIGH, MEDIUM, LOW. At most twenty rows; the rest are counted in the
"Not shown" line. The recommendation is "Proceed" only when there is no CRITICAL and no HIGH finding; otherwise
it names the finding IDs to fix, nothing more. The report stays under 60 lines. Attaching the report as a
comment is the caller's decision, done outside the skill.

## 3. Conventions written into `create-prd`

These go into the "[Task] guidelines" list of Step 5 and the creation list of Step 6.

- Story: one entry under the PRD's core features that an end user can exercise on its own. The PRD still
  uses the example structure, not user-story prose; the story is a unit of grouping, not a writing style.
- Milestones: when the PRD has more than one story, Step 6 creates milestones in this order with
  `save_milestone`: `Setup`, `Foundational`, one `Story: <name>` per story, `Polish`. Every issue is created
  with `milestone` set. Setup holds scaffolding that no story needs alone; Foundational holds what every story
  depends on; Polish holds cross-cutting finish work and the E2E task. A single-story feature creates no
  milestones.
- Titles: `T001: <sentence case title>`, three digits, numbered in implementation order and unique in the
  project. Numbers are never reused or shifted; a task added later takes the next free number. The "Open
  clarifications" issue carries no number.
- `parallel-ok`: a label applied in Step 6 to a task that depends on no unfinished task and names no file
  that another open task in the same milestone names. The task paragraph lists the files or folders it will
  touch so the check is possible; the team lead may run two `parallel-ok` tasks side by side. Created on the
  team by 6b if missing, description "Safe to run alongside its neighbours in the same milestone".
- Story tag: the milestone is the story tag; no `[US1]` text in titles.

## 4. What 6b does with this file

1. Edit `create-prd` Steps 5 and 6 as in sections 1 and 3, keeping the rest of the skill unchanged.
2. Create `.claude/skills/analyze-feature/SKILL.md` from section 2, under 120 lines.
3. Create the two labels and confirm the "Constitution" document exists with the version line of
   `.claude/reference/constitution.md`.
4. Dry run on `entra-directory-policy`: clarify questions answered in the session and not written back; the
   analyze report attached as a comment on that project; no issue changed.
