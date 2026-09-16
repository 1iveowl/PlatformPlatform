// Verifies that a run produced every required harness result for one browser and that each passed on this commit, then
// writes a compact matrix of them. A missing result, a result from another commit, a run that executed fewer cases than
// its script expects, or a failed result fails this check, so a skipped or zero-case run can never read as green.
//
// Run after the harness scripts, through the developer CLI from the repository root:
//   dotnet run --project developer-cli -- blazor-harness verify-results --browser chromium --label published \
//     --require trimmed-smoke-{browser}-en-US,trimmed-smoke-{browser}-da-DK,antiforgery-{browser}
// {browser} in a required name is replaced by the browser. Writes security-matrix-<browser>-<label>.json under
// .workspace/blazor-tests/ and, on GitHub, appends the matrix to the job summary.

import { appendFileSync, existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { parseArguments, resultsFolder, runCommit, writeResult } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium", label: "local" });
if (typeof options.require !== "string" || options.require.length === 0) throw new Error("Name the required results with --require <name,name>.");

const { commit } = runCommit();
const required = options.require.split(",").map((name) => name.trim().replaceAll("{browser}", options.browser));
const results = required.map((name) => {
  const file = path.join(resultsFolder, `${name}.json`);
  if (!existsSync(file)) return { name, passed: false, problem: "missing" };
  const result = JSON.parse(readFileSync(file, "utf8"));
  const entry = {
    name,
    passed: false,
    commit: result.commit,
    browser: result.browser,
    browserVersion: result.browserVersion,
    culture: result.culture ?? null,
    hostEnvironment: result.hostEnvironment ?? null,
    buildConfiguration: result.buildConfiguration ?? result.hostConfiguration ?? null,
    caseCount: result.caseCount,
    expectedCaseCount: result.expectedCaseCount
  };
  if (result.commit !== commit) entry.problem = `result of commit ${result.commit}, not ${commit}`;
  else if (result.browser !== options.browser) entry.problem = `result of browser ${result.browser}`;
  else if (!(result.caseCount >= result.expectedCaseCount && result.caseCount > 0)) entry.problem = `${result.caseCount} of ${result.expectedCaseCount} cases ran`;
  else if (result.passed !== true) entry.problem = "failed";
  else entry.passed = true;
  return entry;
});

const verdict = writeResult(`security-matrix-${options.browser}-${options.label}.json`, { browser: options.browser, label: options.label, results }, required.length);
const rows = results.map((entry) => `| ${entry.name} | ${entry.culture ?? ""} | ${entry.hostEnvironment ?? ""} | ${entry.caseCount ?? ""} | ${entry.passed ? "passed" : entry.problem} |`);
const summary = [`### Security matrix: ${options.browser}, ${options.label}, commit ${commit}`, "", "| Result | Culture | Host | Cases | Verdict |", "| --- | --- | --- | --- | --- |", ...rows, ""].join("\n");
console.log(summary);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
console.log(`Result file: ${verdict.resultFile}`);
process.exitCode = verdict.passed ? 0 : 1;
