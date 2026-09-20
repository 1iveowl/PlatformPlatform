---
name: create-prd
description: Create a product requirement document (PRD) for a new feature. Use when the user wants to create a new feature, plan a feature, or write a PRD.
allowed-tools: *
---

# Create PRD Workflow

Your job is to work with the user through an interactive wizard to create a high-level PRD using language that is easy to understand for non-technical people. The PRD defines a [feature] with all [tasks] to be created in `[PRODUCT_MANAGEMENT_TOOL]`.

Team leads: execute this workflow directly. Do not delegate it.

## Mandatory Preparation

1. **Read [PRODUCT_MANAGEMENT_TOOL]-specific guide** at `/.Codex/reference/product-management/[PRODUCT_MANAGEMENT_TOOL].md` to understand terminology, status mapping, ID format, and MCP configuration.

## Workflow

Follow the steps below to create the PRD.

### Step 1: Initialize `[PRODUCT_MANAGEMENT_TOOL]`

Follow initialization steps in `/.Codex/reference/product-management/[PRODUCT_MANAGEMENT_TOOL].md`.

### Step 2: Ask what [feature] to build

Use the AskUserQuestion tool to ask the user what [feature] they want to build:

```
AskUserQuestion with:
- question: "What feature would you like to build?"
- header: "Feature"
- multiSelect: false
- options:
  - label: "New feature", description: "Create a new feature"
  - label: "Enhancement", description: "Enhance existing functionality"
```

Users will typically use the custom text option to describe their [feature].

**If the user's answer comes back empty:**
- Tell the user to enable Plan Mode and try again
- STOP the workflow

**If you receive a valid answer:**
- Use the text they entered as the [feature] description for research

### Step 3: Research and understand the [feature]

Conduct deep research for a feasible solution that takes the existing codebase and [features] into consideration:
- Understand the user's requirements and business context
- Investigate the current state and implementation in the codebase:
  - Specify which self-contained system (e.g., `main`, `account`) the [feature] belongs to. Back-office features live under `account/Core/Features/BackOffice/` and are served on the back-office host.
  - Respect the multi-tenant nature: design [features] to work for one tenant by default, unless otherwise specified
- Use MCP tools (like context7 for library docs), Perplexity for online research, or web research for best practices and technologies
- Read relevant code files and rule files to understand patterns and conventions

### Step 4: Interactive requirements wizard

Now that you've done research, ask the user ALL required questions in ONE single AskUserQuestion call:

```
AskUserQuestion with 3 questions:

Question 1 - Feature name:
- question: "What is the name of this feature? (Use sentence case, e.g., 'User management' not 'User Management')"
- header: "Feature name"
- multiSelect: false
- options:
  - label: "Custom name", description: "Enter your feature name"

Question 2 - Self-contained system (put the most likely SCS first based on research):
- question: "Which self-contained system (SCS) should this feature belong to?"
- header: "SCS"
- multiSelect: false
- options:
  - label: "account", description: "Tenant and user management system (also hosts the back-office surface for support and system admin tools)"
  - label: "main", description: "Primary shell application where you build your product"
  - label: "[Suggested SCS based on research]", description: "Based on my analysis"

Question 3 - E2E tests:
- question: "Should this PRD include Playwright end-to-end tests?"
- header: "E2E Tests"
- multiSelect: false
- options:
  - label: "Yes", description: "Include E2E tests as a separate [task]"
  - label: "No", description: "Skip E2E tests for now"
```

**Ask additional questions:**

After the first 3 questions, ask additional relevant questions to gather comprehensive requirements. Use multiple AskUserQuestion calls (max 4 questions per call, max 4 options per question).

Ask as many questions as needed to understand:
- User roles and permissions
- Complexity level (simple CRUD, workflow-based, complex logic)
- Integration points with existing features
- Validation rules and constraints
- Edge cases to consider
- Data relationships and dependencies

**The more questions you ask, the better the PRD.**

**Implementation approach:**
```
AskUserQuestion with:
- question: "Should we create frontend mockups first for UI/UX exploration?"
- header: "Approach"
- multiSelect: false
- options:
  - label: "Yes", description: "Frontend mockups first to validate UI/UX before backend"
  - label: "No", description: "Backend-first approach (default)"
```

### Step 5: Draft the complete PRD

Based on all the research and user answers, draft the complete PRD.

**Drafting rule, never guess:** an unspecified point that would change a [task]'s scope, a business rule or an acceptance criterion is written inline as `[NEEDS CLARIFICATION: <one-line question>]` at the place in the text where the answer belongs. A point that only needs a sensible default gets the default with "assumed:" in front of it; it is not a marker. Markers are the only input to Step 5a, so this rule is what keeps the clarify step short.

**Create the PRD content following the [example PRD structure](/.Codex/reference/samples/example-prd.md):**

1. **High-level PRD description:**
   - Use sentence case for level-1 headers
   - Stay at a high level—no implementation details or code examples
   - Use correct domain terminology: multi-tenant, self-contained system, shared kernel, tenant, user, etc.
   - Specify which self-contained system(s) are in scope
   - Avoid repetition

2. **[Tasks] section** structured based on wizard answers:

   **Examples based on common patterns:**

   **Example 1 - Backend-first approach (default):**
   - Backend implementation
   - Frontend implementation
   - E2E tests (if E2E tests selected)

   **Example 2 - Frontend-first approach:**
   - Frontend mockups/prototypes with static data
   - Backend implementation based on frontend contract
   - Integration (connect frontend to backend)
   - E2E tests (if E2E tests selected)

   **Example 3 - Backend-only [feature]:**
   - Backend implementation (API endpoints, commands, queries, migrations, tests)

   **Example 4 - Large complex [feature]:**
   - Backend core functionality
   - Frontend core UI
   - Backend advanced functionality
   - Frontend advanced features
   - E2E tests (if E2E tests selected)

   **Note:** These are examples only. Adapt the [task] structure to match the actual [feature] requirements, scope, and user answers. All work is sequential -- one [task] fully completed before the next starts.

3. **[Task] guidelines:**
   - Each [task] should be a logical grouping (e.g., "all backend", "all frontend", "all e2e tests")
   - Keep [tasks] focused (one commit per [task])
   - Write a clear paragraph describing what each [task] delivers
   - Each [task] represents a complete vertical slice that can be implemented, reviewed, and committed independently
   - Repeat all relevant business rules in each task description (permissions, validations, constraints)
   - Engineers/reviewers only read the task description, not the feature overview
   - **List [tasks] in implementation order** (the order they should be implemented)
   - E2E tests should typically be the final [task]
   - **Important:** When using MCP-based `[PRODUCT_MANAGEMENT_TOOL]`, create [tasks] in the same order they appear in the PRD—this defines the implementation sequence
   - **Story:** one entry under the PRD's core features that an end user can exercise on its own. The PRD still uses the example structure, not user-story prose; the story is a unit of grouping, not a writing style
   - **Milestones:** when the PRD has more than one story, group [tasks] into phases in this order: `Setup` (scaffolding no story needs alone), `Foundational` (what every story depends on), one `Story: <name>` per story, `Polish` (cross-cutting finish work and the E2E [task]). A single-story [feature] has no milestones. The milestone is the story tag; never put `[US1]` style text in titles
   - **Titles:** `T001: <sentence case title>`, three digits, numbered in implementation order and unique in the [feature]. Numbers are never reused or shifted; a [task] added later takes the next free number
   - **Files touched:** each [task] paragraph lists the files or folders it will touch, so Step 6 can decide `parallel-ok`

**Example of WRONG task description (missing business rules):**
```
### 1. Backend for team management

This task implements team CRUD operations with API endpoints and tests.

- Create Team aggregate
- Create CreateTeam command
- Create API endpoints
- Create tests
```

**Example of CORRECT task description (includes business rules):**
```
### 1. Backend for team management

This task implements team CRUD operations with API endpoints and tests. Teams are managed by Tenant Owners and Admins only. Team names must be unique within a tenant.

- Create Team aggregate with name uniqueness validation
- Create CreateTeam command with Owner/Admin permission guard
- Create UpdateTeam command with Owner/Admin permission guard
- Create DeleteTeam command with Owner/Admin permission guard
- Create API endpoints for all operations
- Create tests covering permissions (403 for non-owners/admins), name uniqueness, tenant isolation
```

4. **Frontend task descriptions - use ASCII art fat marker sketches:**

For frontend tasks, include ASCII art fat marker sketches showing UI layout and components:

```
### 2. Frontend for user management

This task implements the Users page UI. Users can only be managed by Tenant Owners or Admins.

┌─────────────────────────────────────────┐
│ Users                   [+ Invite user] │
├─────────────────────────────────────────┤
│ ┌─────────────────────────────────────┐ │
│ │ Email           Name         Role   │ │
│ ├─────────────────────────────────────┤ │
│ │ admin@...       John Doe     Owner  │ │
│ │ member@...      Jane Smith   Member │ │
│ └─────────────────────────────────────┘ │
└─────────────────────────────────────────┘

- Add Users navigation menu item
- Create Users page with table
- Create CreateUserDialog (validates email uniqueness)
- Show/hide [+ Invite user] button based on role (Owner/Admin only)
- Create UserDetailsSidePane
- Integrate all API operations
```

ASCII sketches help engineers visualize the UI before coding.

### Step 5a: Clarify

Runs before the PRD is shown for approval, so the user approves the clarified text.

**Scan** the description and every [task] paragraph against these nine categories, in this order:

1. Functional scope: what is in and what is out
2. Data model: entities, fields, identity, uniqueness, retention
3. UX flow: entry points, states, errors, empty states
4. Non-functional: performance, limits, availability, localisation
5. Integration: other self-contained systems, external providers, events
6. Edge cases: concurrency, partial failure, re-entry, deletion
7. Constraints: permissions, tenancy, feature flags, compliance
8. Terminology: names that must match the glossary or the code
9. Completion signals: how the user knows the [feature] is done and how it is tested

Record Clear, Partial or Missing per category in a nine-row table kept in the session, not in the PRD. Every marker maps to one category. Rank the candidate questions: one that changes the [task] split or an acceptance criterion outranks one that changes a business rule, which outranks wording. Keep the top five. If no category is Partial or Missing and no marker exists, say "No clarifications needed" and go to Step 5b.

**Ask** the questions one at a time, one AskUserQuestion call per question, never batched:

```
AskUserQuestion with:
- header: "<category name>"
- question: "<marker text, rewritten so it can be answered by choosing an option or a phrase of five words or fewer> (or type up to five words)"
- multiSelect: false
- options: two to four concrete answers, each with a one-line consequence as description; the option the research suggests goes first, its label ending in "(recommended)"
```

The free-text answer is the five-words-or-fewer phrase. Stop after five questions, or earlier when the user answers "stop", "skip the rest" or "proceed". Never ask a sixth question in the same session, even if markers remain.

**Record** after each answer, before asking the next question:
- Replace the marker in the draft with the resolved text, in the sentence where it sat
- Append one line under `## Clarifications` / `### <today's date, YYYY-MM-DD>` in the form `- <Category>: Q: <question> A: <answer>`. The section is the last section of the description, after the core changes and before "Tasks overview", so Step 6 stores it as part of the [feature] description. A later clarify session on the same [feature] adds a new dated sub-heading rather than editing an earlier one

Markers not resolved (beyond the five, or skipped) stay in the text unchanged and are handled in Step 6.

### Step 5b: Get approval

Show the complete PRD to the user - display the full content including all [tasks] with their descriptions.

**Ask for approval:** "Does this PRD look good?" (Yes/No)
- If No: Ask what to change, update the PRD content, show again, repeat approval
- If Yes: Continue to Step 6

### Step 6: Create [feature] and [tasks] in [PRODUCT_MANAGEMENT_TOOL]

Follow your [PRODUCT_MANAGEMENT_TOOL]-specific guide at `/.Codex/reference/product-management/[PRODUCT_MANAGEMENT_TOOL].md` to understand how to create items based on the PRD.

Create:
- [feature] with name=[feature name from Step 4 wizard], assign to "me"
- Milestones, only when the PRD has more than one story, in this order with `save_milestone`: `Setup`, `Foundational`, one `Story: <name>` per story, `Polish`
- [task] for each [task] in the PRD with:
  - Title: `T001: [task title]` (three-digit number in implementation order, sentence case)
  - Description: [task description paragraph] + [subtask bullets] (use bullets, NOT checkboxes)
  - Link to parent [feature]
  - `milestone` set, when milestones exist
  - Label `parallel-ok` when the [task] depends on no unfinished [task] and names no file or folder that another open [task] in the same milestone names
  - Assign to "me"
- Initialize all items in [Planned] status, in the current iteration/sprint

**Remaining markers**, after the [tasks] are created:
- Every [task] whose description still contains `[NEEDS CLARIFICATION: ...]` gets `addLabels: ["needs-clarification"]`
- Create one [task] titled `Open clarifications` (no `T` number, so it is never picked as a [task] to implement) in the [feature], in [Planned], with a bullet per marker: the category, the question and the [task] it affects. Its `blocks` list holds every labelled [task]. Markers in the [feature] description but in no [task] are listed in the same item and block nothing
- When no marker remains, do not create it

Each [task] description must include:
1. A paragraph explaining what the task delivers
2. Bullet points (NOT checkboxes) listing the subtasks for implementation guidance

After creating all [tasks], update each [feature]'s description in [PRODUCT_MANAGEMENT_TOOL] with the full PRD content for that [feature] -- the intro paragraph, overview section, and core changes section (everything above the "Tasks overview" heading). This ensures the PRD context is available to anyone viewing the [feature] in [PRODUCT_MANAGEMENT_TOOL].

**Inform user:** The [feature] and all [tasks] have been created in [PRODUCT_MANAGEMENT_TOOL]. Ask the user if they would like to start implementing the first task now.

## Guidelines

✅ DO:
- Follow the exact structure in the example PRD
- Conduct deep research by reading code, consulting rule files, and using MCP tools
- Specify the self-contained system for the [feature]
- Respect multi-tenant design by default
- Keep the PRD high level without code snippets
- Ask comprehensive questions in Step 4 to gather all requirements
- Show PRD for approval (Step 5b) before creating anything
- Use the AskUserQuestion tool for all wizard questions in Plan Mode

❌ DON'T:
- Write PRDs as user stories—use the example structure
- Include implementation details or code examples in the PRD
- Skip research—always understand the problem first
- Ignore rule files
- Repeat information across sections
- Write titles in Title Case—use sentence case
- Create [feature] or [tasks] in `[PRODUCT_MANAGEMENT_TOOL]` before getting PRD approval in Step 5b
- Rename the file—must be `prd.md`
- Save questions in the PRD file
- Create [tasks] that split tests, implementation, and migrations across separate [tasks]—each [task] must be a complete vertical slice
- Ask the user clarifying questions before Step 4

Do the research. Read code and rule files. Ask comprehensive questions. Create excellent PRDs.