import { test } from "@playwright/test";
import { uniqueEmail } from "@shared/e2e/utils/test-data";

/**
 * A unique email address namespaced by the running test's project (browser, culture and lane), worker, retry and a random
 * part, so projects running in parallel, locally or on CI, never share a disposable identity. The tag stays short, for
 * example "chdadks0r1" for chromium-da-DK-smoke on worker 0 in retry 1, so prefixed addresses keep a local part below 64
 * characters.
 */
export function uniqueBlazorEmail(): string {
  const { project, workerIndex, retry } = test.info();
  const [browser, language, region, lane] = project.name.toLowerCase().split("-");
  const tag = `${browser.slice(0, 2)}${language}${region}${lane.charAt(0)}${workerIndex}r${retry}`;
  const [localPart, domain] = uniqueEmail().split("@");
  const random = Math.random().toString(36).slice(2, 6);
  return `${localPart}.${tag}${random}@${domain}`;
}
