// Checks 3b and 3c of the staging acceptance: a Container Apps job created from probe-job.yaml beside this file, started
// once, its log read back and the job deleted again. The job is an Azure change, so it runs only after the owner types its
// name on the terminal for this run; without that it is recorded as not run, never as passed.
//
// The functions passed in:
// - azure: createAzure of support/deployed.mjs, { json(arguments), raw(arguments) }
// - ask(question): the terminal question of support/deployed.mjs, null without a terminal
// - log(message), and now() and sleep(ms) for the poll, each defaulting to the real one

import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";

export const probeName = "Forged headers inside the environment (3b and 3c)";
export const probeChecks = [
  { id: "3b", name: "Forged Host on the internal account API" },
  { id: "3c", name: "Forged headers on the back-office app from inside" }
];

const templateFile = path.join(import.meta.dirname, "probe-job.yaml");
const pollMs = 10_000;
const executionTimeoutMs = 10 * 60_000;
const finalStates = ["Succeeded", "Failed", "Stopped", "Degraded"];

export const probeJobName = (now = new Date()) => `bo-probe-${now.toISOString().replace(/[^0-9]/g, "").slice(2, 12)}`;

// Describes the Azure change and asks for the job name; returns { approved: true } or { approved: false, reason }
export async function askProbeApproval({ jobName, resourceGroup, environmentName, ask, log = console.log }) {
  log(`
Checks 3b and 3c need a probe job inside the Container Apps environment. It is an Azure change:
  create  Container Apps job ${jobName} in ${resourceGroup}, environment ${environmentName}
  image   the curl image pinned in blazor/tests/staging/probe-job.yaml, manual trigger, no ingress, no secret
  then    start one execution, read its log, delete the job`);
  const approval = await ask(`Type the job name (${jobName}) to approve this run, or press Enter to skip: `);
  if (approval === jobName) return { approved: true };
  return { approved: false, reason: approval === null ? "no terminal to ask for the probe job's approval" : "the owner did not approve the probe job for this run" };
}

// The check body: creates the job from the template filled with placeholders ({ LOCATION: ..., ... } for {{LOCATION}}),
// runs one execution and checks its answers. Deletes the job whenever it was created; a failed delete fails the check and
// names the job to delete by hand.
export async function runProbeJob({ azure, resourceGroup, jobName, placeholders, log = console.log, now = Date.now, sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)) }) {
  let filled = readFileSync(templateFile, "utf8");
  for (const [name, value] of Object.entries(placeholders)) filled = filled.replaceAll(`{{${name}}}`, value);
  const folder = mkdtempSync(path.join(os.tmpdir(), "probe-job-"));
  const file = path.join(folder, "probe-job.yaml");
  writeFileSync(file, filled);
  let created = false;
  try {
    azure.raw(["containerapp", "job", "create", "--name", jobName, "--resource-group", resourceGroup, "--yaml", file]);
    created = true;
    const execution = azure.json(["containerapp", "job", "start", "--name", jobName, "--resource-group", resourceGroup]);
    const executionName = execution.name ?? execution.id?.split("/").pop();
    const deadline = now() + executionTimeoutMs;
    let status;
    do {
      await sleep(pollMs);
      status = azure.json(["containerapp", "job", "execution", "show", "--name", jobName, "--resource-group", resourceGroup, "--job-execution-name", executionName]).properties.status;
    } while (!finalStates.includes(status) && now() < deadline);
    if (status !== "Succeeded") throw new Error(`The probe execution ${executionName} ended ${status}.`);
    const lines = azure
      .raw(["containerapp", "job", "logs", "show", "--name", jobName, "--resource-group", resourceGroup, "--execution", executionName, "--container", "probe", "--format", "text"])
      .split("\n")
      .filter((line) => line.includes("PROBE "))
      .map((line) => line.slice(line.indexOf("PROBE ") + 6).trim());
    if (!lines.includes("done")) throw new Error(`The probe log has no end marker: ${lines.join(" | ")}.`);
    const answers = lines
      .filter((line) => line !== "done")
      .map((line) => {
        const [probe, answerStatus, url, host, accept] = line.split(" ");
        return { check: probe, status: Number(answerStatus), url, host, accept };
      });
    const wrong = answers.filter((answer) => (answer.check === "3b" ? ![401, 404].includes(answer.status) : ![302, 401].includes(answer.status)));
    if (answers.length !== 5 || wrong.length > 0) throw new Error(`Answers: ${JSON.stringify(answers)}.`);
    return { job: jobName, execution: executionName, answers };
  } finally {
    rmSync(folder, { recursive: true, force: true });
    if (created) {
      try {
        azure.raw(["containerapp", "job", "delete", "--name", jobName, "--resource-group", resourceGroup, "--yes"]);
        log(`Deleted the probe job ${jobName}.`);
      } catch (error) {
        throw new Error(`The probe job ${jobName} could not be deleted; delete it by hand: ${error.message}`);
      }
    }
  }
}
