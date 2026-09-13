---
name: analyze-feature
description: Read-only consistency report for a Linear project: constitution conflicts, duplicated requirements, missing or untraceable acceptance criteria, terminology drift. Use after create-prd and before the first task starts.
allowed-tools: Read, Grep, Glob, mcp__linear-server__get_project, mcp__linear-server__list_documents, mcp__linear-server__get_document, mcp__linear-server__list_issues, mcp__linear-server__get_issue, mcp__linear-server__list_comments
---

# Analyze Feature Workflow

Report inconsistencies across a [feature]'s description, documents and [tasks] in `[PRODUCT_MANAGEMENT_TOOL]`, checked against the constitution.

This skill writes nothing to Linear or disk; the report is its final message. If asked to fix a finding, refuse and name the skill or the manual edit that does it.

## Input

One argument: a project name, ID or slug. Without an argument, ask for one and stop.

## Procedure

### Step 1: Constitution

Read `.claude/reference/constitution.md`. Fetch the Linear document "Constitution" on the team (`list_documents` with `query: "Constitution"`, then `get_document`). Compare the version lines; a mismatch is a HIGH finding on its own. The repository copy is the one used for the checks.

### Step 2: Project

- `get_project` with `includeMilestones` and `includeResources`
- `get_document` for each attached document
- `list_issues` with `project` and `fields` limited to id, title, description, labels, status, projectMilestone
- `get_issue` with `includeRelations` only for [tasks] labelled `needs-clarification`

Read each item once.

### Step 3: Inventory

Keep in the session:

- Requirements: every sentence in the description and documents containing must, never, only, always, will or shall, numbered R1, R2, ...
- Acceptance criteria: per [task], the bullets under a heading containing "acceptance" or, failing that, the bullets after the first paragraph, numbered `<id>-AC1`, ...
- Terms: nouns that name a domain concept, checked against `.claude/skills/codebase-overview/references/glossary.md`

### Step 4: Checks

Each finding gets a stable ID prefix and a severity:

- **D (duplicate):** the same requirement stated in two places with different wording, or the same acceptance criterion in two [tasks]. MEDIUM; HIGH when the two versions disagree.
- **M (missing):** a [task] with no acceptance criteria, or with criteria that cannot be observed (no input, no expected result). HIGH.
- **T (traceability):** an acceptance criterion that no requirement explains, or a requirement that no [task] covers. MEDIUM.
- **N (naming):** two names for one concept, a name absent from the glossary, a [task] title not in the `T001: ...` form or not in sentence case. LOW; MEDIUM when both names appear in [tasks].
- **C (constitution):** a conflict with any principle. Always CRITICAL. Examples: past tense for work not yet built; a code claim with no "Verified at"; a [task] that tells the engineer to skip or weaken a test or to drop review; a [task] that spans more than one commit or session; an item that depends on a file outside the repository; a guessed answer where a `[NEEDS CLARIFICATION]` marker belongs; a mention of a tool vendor.
- **S (structure):** a marker without the `needs-clarification` label or without a blocking relation from "Open clarifications"; a gap or duplicate in `T` numbers; a `parallel-ok` [task] that names a file another open [task] in the same milestone names; a multi-story [feature] without milestones. MEDIUM.

### Step 5: Report

The final message and nothing else, in this fixed shape:

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

- Order rows by severity: CRITICAL, HIGH, MEDIUM, LOW
- At most twenty rows; the rest are counted in the "Not shown" line
- The recommendation is "Proceed" only when there is no CRITICAL and no HIGH finding; otherwise it names the finding IDs to fix, nothing more
- Keep the report under 60 lines

Attaching the report as a comment is the caller's decision, done outside this skill.
