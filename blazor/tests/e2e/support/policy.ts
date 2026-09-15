import { expect, type Page } from "@playwright/test";

declare global {
  interface Window {
    __policyViolations?: string[];
  }
}

/**
 * Record every securitypolicyviolation event of every document the page loads from now on. The record belongs to one
 * document, so expect it to be empty before each full navigation or reload as well as at the end.
 * @param page Playwright page instance, before its first navigation
 */
export async function trackPolicyViolations(page: Page): Promise<void> {
  await page.addInitScript(() => {
    window.__policyViolations = [];
    document.addEventListener("securitypolicyviolation", (event) => {
      window.__policyViolations!.push(`${event.effectiveDirective} ${event.blockedURI} ${event.sample}`);
    });
  });
}

/**
 * Expect the current document to have raised no securitypolicyviolation event and to carry no style attribute in its body.
 * The runtime's loading progress custom properties on the root element are set through the style object, which the policy
 * allows, and are left out
 * @param page Playwright page instance whose document was loaded after trackPolicyViolations
 */
export async function expectNoPolicyViolations(page: Page): Promise<void> {
  const state = await page.evaluate(() => ({
    violations: window.__policyViolations ?? ["not tracked"],
    styleAttributes: [...document.body.querySelectorAll("[style]")].map((element) => element.outerHTML.slice(0, 120))
  }));

  expect(state).toEqual({ violations: [], styleAttributes: [] });
}
