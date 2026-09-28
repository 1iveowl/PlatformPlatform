import { mkdir, open, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { expect, type Page } from "@playwright/test";
import { getBackOfficeBaseUrl } from "@shared/e2e/utils/constants";
import { readBackOfficeAntiforgeryToken } from "./feature-flags";

/**
 * The flag the back-office feature flag specification activates, re-targets and deactivates: a kill-switch A/B test on the
 * user scope that no other specification, React or Blazor, depends on. The reconciler creates it inactive at 0 %.
 */
export const administeredFeatureFlagKey = "experimental-ui";

/**
 * A flag's global state as the back office reports it
 */
export interface FeatureFlagState {
  isActive: boolean;
  rolloutPercentage: number;
}

/**
 * How long a project waits for another project's hold on a flag, and after how long a hold is taken to be left behind by a
 * run that was killed. A hold lasts one test's writes, well under a minute.
 */
const lockWaitMilliseconds = 480_000;
const staleLockMilliseconds = 180_000;
const lockPollMilliseconds = 500;

/**
 * Run the given writes while this project alone may change the flag's global state. Every browser and culture project runs
 * the specification at the same time, and a flag's activation and rollout are global, so without this one project's
 * deactivation would land between another project's activation and its assertion. The hold is a file created exclusively in
 * the operating system's temporary folder, named by the flag and the back-office host so that two stacks on one machine do
 * not wait for each other, and it is removed when the writes end, whatever their outcome.
 * @param flagKey The registry key of the flag
 * @param writes The steps that change and assert the flag's global state
 */
export async function withFeatureFlagHold<T>(flagKey: string, writes: () => Promise<T>): Promise<T> {
  const folder = path.join(tmpdir(), "blazor-e2e-holds");
  await mkdir(folder, { recursive: true });
  const holdPath = path.join(folder, `${new URL(getBackOfficeBaseUrl()).host.replace(/[^a-z0-9]/gi, "-")}-${flagKey}.hold`);
  const deadline = Date.now() + lockWaitMilliseconds;

  while (true) {
    try {
      const handle = await open(holdPath, "wx");
      await handle.close();
      break;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "EEXIST") throw error;
      const holdAge = await stat(holdPath).then((stats) => Date.now() - stats.mtimeMs, () => 0);
      if (holdAge > staleLockMilliseconds) await rm(holdPath, { force: true });
      if (Date.now() > deadline) throw new Error(`Another project held '${flagKey}' for longer than ${lockWaitMilliseconds} ms.`);
      await new Promise((resolve) => setTimeout(resolve, lockPollMilliseconds));
    }
  }

  try {
    return await writes();
  } finally {
    await rm(holdPath, { force: true });
  }
}

/**
 * The flag's global activation and rollout percentage, read through the back-office API from a signed-in back-office document
 * @param page Playwright page instance signed in to the back office
 * @param flagKey The registry key of the flag
 */
export async function readFeatureFlagStateThroughBackOffice(page: Page, flagKey: string): Promise<FeatureFlagState> {
  const response = await page.evaluate(async () => {
    const answer = await fetch("/api/back-office/feature-flags?IncludeDeleted=true", { credentials: "same-origin" });
    return { status: answer.status, body: await answer.text() };
  });

  expect(response.status, response.body).toBe(200);
  const { flags } = JSON.parse(response.body) as { flags: { key: string; isActive: boolean; rolloutPercentage: number | null }[] };
  const flag = flags.find((candidate) => candidate.key === flagKey);
  expect(flag, `The back office lists '${flagKey}'`).toBeDefined();
  return { isActive: flag!.isActive, rolloutPercentage: flag!.rolloutPercentage ?? 0 };
}

/**
 * Whether the flag is on for one user of the app, as the account API evaluates it for that user's next access token, read
 * through the back office's user feature flags
 * @param page Playwright page instance signed in to the back office
 * @param userId The id of the app user
 * @param flagKey The registry key of the flag
 */
export async function readUserFeatureFlagThroughBackOffice(page: Page, userId: string, flagKey: string): Promise<boolean> {
  const response = await page.evaluate(async (id) => {
    const answer = await fetch(`/api/back-office/users/${id}/feature-flags`, { credentials: "same-origin" });
    return { status: answer.status, body: await answer.text() };
  }, userId);

  expect(response.status, response.body).toBe(200);
  const { flags } = JSON.parse(response.body) as { flags: { flagKey: string; isEnabled: boolean }[] };
  return flags.find((flag) => flag.flagKey === flagKey)?.isEnabled === true;
}

/**
 * Put the flag's global state back through the back-office API, from the React back office's administrator document, whose
 * head carries the antiforgery token its writes need. Used for the precondition and the teardown of a hold, never for the
 * steps under test.
 * @param page Playwright page instance of a React back-office administrator
 * @param flagKey The registry key of the flag
 * @param state The activation and rollout percentage to leave behind
 */
export async function setFeatureFlagStateThroughBackOffice(page: Page, flagKey: string, state: FeatureFlagState): Promise<void> {
  const antiforgeryToken = await readBackOfficeAntiforgeryToken(page);
  const statuses = await page.evaluate(
    async ({ flagKey, state, antiforgeryToken }) => {
      const headers = { "content-type": "application/json", "x-xsrf-token": antiforgeryToken };
      const rollout = await fetch(`/api/back-office/feature-flags/${flagKey}/rollout-percentage`, {
        method: "PUT",
        credentials: "same-origin",
        headers,
        body: JSON.stringify({ rolloutPercentage: state.rolloutPercentage })
      });
      const activation = await fetch(`/api/back-office/feature-flags/${flagKey}/${state.isActive ? "activate" : "deactivate"}`, {
        method: "PUT",
        credentials: "same-origin",
        headers
      });
      return [rollout.status, activation.status];
    },
    { flagKey, state, antiforgeryToken }
  );

  expect(statuses.every((status) => status >= 200 && status < 300), `Setting '${flagKey}' answered ${statuses.join(", ")}`).toBe(true);
  expect(await readFeatureFlagStateThroughBackOffice(page, flagKey)).toEqual(state);
}
