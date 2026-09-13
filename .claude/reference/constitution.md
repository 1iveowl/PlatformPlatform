# Constitution

Version 1.0.0, ratified 2026-09-12.

These principles are non-negotiable for every session, agent and skill that works in this repository. A plan,
a task or a review that conflicts with one of them is wrong until the conflict is removed or this document
is amended with a new version line. The same text is published as the Linear document "Constitution" on the
PlatformPlatform team; when the two differ, the higher version wins and the other is brought up to date.

## 1. Verify before asserting

Every path, name, route, column and behaviour is checked against the code before it is stated, even when
asked to answer from memory. Nothing is read from a prior session, a cached summary or a note without
re-checking the claim that matters. Only files inside this repository are read unless the task says otherwise.

## 2. Tense and evidence

A statement about the code names what confirmed it and when: "Verified at <commit>: ...". Anything not
confirmed is marked as an assumption so a reader knows which sentences to re-check. Text about work that has
not happened stays in the future tense: "the next task will add the column", never "added the column".
Completed items are frozen with an as-built delta rather than rewritten; the record of what was planned
against what was built is worth keeping.

## 3. No shortcut through quality

No test is weakened, skipped, marked flaky or deleted to make a run pass or to save tokens. No independent
review is dropped, shortened to a skim or merged into the engineer's own session to save tokens. Build, then
format, lint and test, run before every commit; a check that was not run is reported as not run.

## 4. The andon cord

On any stop condition the work stops and a human or the team lead is asked: uncommitted changes at start,
a task in an unexpected status, a failing check with no obvious cause, a rule that cannot be followed, a
requirement that admits more than one reading, or an instruction to bypass a guard. Guessing, working around
a failed tool and reporting "done" with a caveat are all worse than stopping.

## 5. Receipts, not transcripts

A final message is a receipt: status, files or commit, checks run with results, blockers, and where the full
logs are. No narration of progress, no restating of the task. Spawn prompts and comments point at the Linear
item and say nothing it already says. Linear writes use the smallest field set and are never re-fetched to
confirm; the save response is the confirmation.

## 6. One task per session

A session takes one task and ends when it is done or blocked. Hand-off goes through Linear and the commit,
never through the transcript. At 200k tokens of context the current step is finished, a hand-off is written
and a fresh session is started. Rewind to a cached prefix is preferred to compaction when the tail is what
should go.

## 7. Source of truth

Linear holds the plan: the project description, its documents and its issues. An engineer or reviewer reads
only the task, so each task repeats every business rule it needs. No item depends on a file outside the
repository or under a git-ignored path. Unspecified points are marked `[NEEDS CLARIFICATION]` and asked,
never guessed.

## 8. Commits and authorship

Nothing is committed, amended, reverted or pushed without an explicit instruction each time. A commit
message is one line, imperative, no body. The human is the author; no tool, model or vendor is named in a
commit, a pull request, a comment or any file on a branch.

## Amendments

An amendment bumps the version (major for a removed or reversed principle, minor for a new one, patch for
wording), updates the ratified date, and is published to the Linear document in the same session.
