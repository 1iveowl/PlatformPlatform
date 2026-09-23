// The device pass in one command on the Mac: the setup probe, then every script for the chosen target, then one verdict.
//
// Runs, in order:
// - probe.sh, the read-only setup report;
// - safari-offline.mjs on real Safari on macOS;
// - safari-push.mjs on real Safari on macOS;
// - safari-offline.mjs in Safari in the iOS Simulator, only with --target simulator or --target all: on the Mac this was
//   written for, safaridriver refuses every Simulator session (README.md), so the default run leaves it out.
//
// The verdict is "passed" when every case of every run passed, "passed with manual cells" when the only cases that did not
// pass are cells a script cannot establish on that browser, each named with its reason, and "failed" otherwise. A run that
// could not start, a missing or stale result file and a script that ran fewer cases than it expects all fail it.
//
// Run on the Mac, from the repository folder:
//   node blazor/tests/device/run.mjs [--target mac|simulator|all] [--device "iPhone 17"] [--upstream 19000]
// It writes device-pass.json beside the result files under .workspace/blazor-tests/device/.

import { spawn } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { checkPreconditions, developerEnvironment, parseArguments, resultsFolder, runCommit, verdicts, writeArtifact } from "./support.mjs";

const options = parseArguments(process.argv.slice(2), { target: "mac", upstream: "19000" });
const scriptFolder = import.meta.dirname;
const forwarded = ["--upstream", String(options.upstream), ...(typeof options.device === "string" ? ["--device", options.device] : [])];

const allRuns = [
  { name: "offline shell, macOS", target: "mac", script: "safari-offline.mjs", resultFile: "safari-offline-shell.json" },
  { name: "push, macOS", target: "mac", script: "safari-push.mjs", resultFile: "safari-push.json" },
  { name: "offline shell, iOS Simulator", target: "simulator", script: "safari-offline.mjs", resultFile: "ios-simulator-offline-shell.json" }
];
if (!["all", "mac", "simulator"].includes(options.target)) throw new Error(`Unknown target ${JSON.stringify(options.target)}; use all, mac or simulator.`);
const runs = allRuns.filter((run) => options.target === "all" || run.target === options.target);

const problems = await checkPreconditions(Number(options.upstream));
if (problems.length > 0) {
  for (const problem of problems) console.log(`SETUP ${problem}`);
  process.exit(2);
}

function runChild(command, argumentList, environment = process.env) {
  return new Promise((resolve) => {
    const child = spawn(command, argumentList, { env: environment, stdio: "inherit" });
    child.once("exit", (code, signal) => resolve(code ?? `signal ${signal}`));
    child.once("error", (error) => resolve(`not started: ${error.message}`));
  });
}

// A result counts only when this run wrote it on this commit; a file left from an earlier run is stale, never read as current
function readResult(run, startedAt, commit) {
  const file = path.join(resultsFolder, run.resultFile);
  if (!existsSync(file)) return { problem: "no result file" };
  if (statSync(file).mtimeMs < startedAt) return { problem: "the result file is from an earlier run" };
  const result = JSON.parse(readFileSync(file, "utf8"));
  if (result.commit !== commit) return { problem: `the result is from ${result.commit}, not ${commit}` };
  return { result };
}

const identity = runCommit();
const startedAt = new Date();
console.log(`== probe`);
const probeExit = await runChild("bash", [path.join(scriptFolder, "probe.sh")], { ...process.env, ...developerEnvironment(), DEVICE_PASS_UPSTREAM: String(options.upstream) });

const outcomes = [];
for (const run of runs) {
  console.log(`\n== ${run.name}`);
  const runStartedAt = Date.now();
  const exitCode = await runChild(process.execPath, [path.join(scriptFolder, run.script), "--target", run.target, ...forwarded]);
  const outcome = { name: run.name, target: run.target, script: run.script, exitCode, resultFile: path.join(resultsFolder, run.resultFile) };
  if (exitCode === 2) {
    outcome.verdict = verdicts.failed;
    outcome.problem = "not run: a setup problem, printed above as SETUP";
  } else {
    const { result, problem } = readResult(run, runStartedAt, identity.commit);
    if (problem !== undefined) {
      outcome.verdict = verdicts.failed;
      outcome.problem = problem;
    } else {
      outcome.verdict = result.verdict;
      outcome.caseCount = result.caseCount;
      outcome.expectedCaseCount = result.expectedCaseCount;
      outcome.failedCases = result.results.filter((entry) => entry.status === "failed").map((entry) => entry.name);
      outcome.manualCells = result.manualCells;
      outcome.failures = result.failures;
      outcome.environment = result.environment;
    }
  }
  outcomes.push(outcome);
}

const failed = outcomes.some((outcome) => outcome.verdict === verdicts.failed);
const withManualCells = outcomes.some((outcome) => outcome.verdict === verdicts.passedWithManualCells);
const verdict = failed ? verdicts.failed : withManualCells ? verdicts.passedWithManualCells : verdicts.passed;
const summaryFile = writeArtifact(
  "device-pass.json",
  JSON.stringify({ ...identity, startedAt: startedAt.toISOString(), finishedAt: new Date().toISOString(), target: options.target, probeExit, runs: outcomes, verdict, passed: verdict === verdicts.passed }, null, 2)
);

console.log("\n== device pass");
for (const outcome of outcomes) {
  const cases = outcome.caseCount === undefined ? "" : ` (${outcome.caseCount} of ${outcome.expectedCaseCount} cases)`;
  console.log(`${outcome.verdict.toUpperCase()} ${outcome.name}${cases}${outcome.problem === undefined ? "" : `: ${outcome.problem}`}`);
  for (const name of outcome.failedCases ?? []) console.log(`  failed: ${name}`);
  for (const cell of outcome.manualCells ?? []) console.log(`  manual: ${cell.name}: ${cell.reason}`);
}
console.log(`${verdict.toUpperCase()} device pass on ${identity.commit}${identity.uncommittedChanges ? " with uncommitted changes" : ""}: ${summaryFile}`);
process.exit(failed ? 1 : 0);
