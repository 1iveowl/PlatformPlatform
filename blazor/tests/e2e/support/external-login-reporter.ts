/// <reference types="node" />
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import type { FullConfig, FullResult, Reporter, Suite, TestCase, TestResult } from "@playwright/test/reporter";

/**
 * The provider specifications whose runs are recorded, by file name, and the tests each one holds (one @smoke, one @comprehensive)
 */
const providerSpecifications: Record<string, string> = {
  "google-oauth-flows.spec.ts": "Google",
  "entra-oauth-flows.spec.ts": "Entra",
  "mitid-login-flows.spec.ts": "MitId",
  "mitid-verification-flows.spec.ts": "MitIdVerification"
};
const testsPerSpecification = 2;

const repositoryRoot = path.resolve(__dirname, "../../../..");
const resultsFolder = path.join(repositoryRoot, ".workspace/blazor-tests");

interface ProviderCase {
  provider: string;
  test: string;
  lane: "smoke" | "comprehensive";
  status: TestResult["status"];
  disabled: boolean;
  mockProviderEvidence: boolean;
}

/**
 * Records every run of the Google, Entra and MitID login specifications and the MitID verification specification as two harness results per browser and culture, in
 * the shape blazor/tests/verify-results.mjs checks, so the strict verdict covers them:
 * - external-login-providers-enabled-<browser>-<culture>.json passes only when every test of the four specifications ran
 *   and passed; a skipped, failed or missing test fails it, so a run with a provider disabled can never satisfy it
 * - external-login-providers-disabled-<browser>-<culture>.json passes only when every test was skipped by its provider-disabled
 *   check after the unavailable behaviour was asserted
 * Each result counts executed, skipped and failed tests per provider and labels the evidence as mock provider evidence.
 * Nothing is written for a run that did not include a provider specification.
 */
export default class ExternalLoginReporter implements Reporter {
  private readonly cases = new Map<string, ProviderCase[]>();

  // A run that includes a provider specification removes the previous results first, so a run that fails before writing can
  // never leave an older result behind to be read as this run's
  onBegin(_config: FullConfig, suite: Suite): void {
    if (!suite.allTests().some((test) => providerSpecifications[path.basename(test.location.file)] !== undefined)) return;
    if (!existsSync(resultsFolder)) return;
    for (const file of readdirSync(resultsFolder).filter((name) => name.startsWith("external-login-providers-"))) {
      rmSync(path.join(resultsFolder, file), { force: true });
    }
  }

  onTestEnd(test: TestCase, result: TestResult): void {
    const provider = providerSpecifications[path.basename(test.location.file)];
    if (provider === undefined) return;
    const project = test.parent.project();
    const browser = project?.use.defaultBrowserType ?? project?.name.split("-")[0] ?? "unknown";
    const culture = project?.use.locale ?? "unknown";
    const key = `${browser}-${culture}`;
    const cases = this.cases.get(key) ?? [];
    // A retried test is recorded once, with its final attempt
    const title = test.titlePath().slice(1).join(" › ");
    const annotations = [...test.annotations, ...result.annotations];
    const existing = cases.findIndex((entry) => entry.provider === provider && entry.test === title);
    const entry: ProviderCase = {
      provider,
      test: title,
      lane: test.tags.includes("@smoke") || title.includes("@smoke") ? "smoke" : "comprehensive",
      status: result.status,
      disabled: annotations.some((annotation) => annotation.type === "provider-disabled"),
      mockProviderEvidence: annotations.some((annotation) => annotation.type === "mock-provider-evidence")
    };
    if (existing >= 0) cases[existing] = entry;
    else cases.push(entry);
    this.cases.set(key, cases);
  }

  onEnd(_result: FullResult): void {
    if (this.cases.size === 0) return;
    const git = (...argumentList: string[]) => execFileSync("git", ["-c", `safe.directory=${repositoryRoot}`, ...argumentList], { cwd: repositoryRoot, encoding: "utf8" }).trim();
    const commit = process.env.GITHUB_SHA ?? git("rev-parse", "HEAD");
    const uncommittedChanges = git("status", "--porcelain").length > 0;
    const expectedCaseCount = Object.keys(providerSpecifications).length * testsPerSpecification;
    mkdirSync(resultsFolder, { recursive: true });

    for (const [key, cases] of this.cases) {
      const [browser, ...cultureParts] = key.split("-");
      const culture = cultureParts.join("-");
      const providers = Object.fromEntries(
        Object.values(providerSpecifications).map((provider) => {
          const ofProvider = cases.filter((entry) => entry.provider === provider);
          return [
            provider,
            {
              executed: ofProvider.filter((entry) => entry.status !== "skipped").length,
              passed: ofProvider.filter((entry) => entry.status === "passed").length,
              skipped: ofProvider.filter((entry) => entry.status === "skipped").length,
              skippedAsDisabled: ofProvider.filter((entry) => entry.status === "skipped" && entry.disabled).length,
              failed: ofProvider.filter((entry) => entry.status !== "passed" && entry.status !== "skipped").length
            }
          ];
        })
      );
      const evidence = "mock provider (__Test_Use_Mock_Provider cookie), not a real provider integration result";
      const write = (lane: "enabled" | "disabled", passedCase: (entry: ProviderCase) => boolean) => {
        const results = cases.map((entry) => ({ ...entry, passed: passedCase(entry) }));
        const failures = results.length < expectedCaseCount ? [`${results.length} of ${expectedCaseCount} provider tests ran`] : [];
        const passed = failures.length === 0 && results.every((entry) => entry.passed);
        const content = {
          commit,
          uncommittedChanges,
          browser,
          culture,
          lane: `providers-${lane}`,
          evidence,
          providers,
          results,
          caseCount: results.length,
          expectedCaseCount,
          failures,
          passed
        };
        writeFileSync(path.join(resultsFolder, `external-login-providers-${lane}-${browser}-${culture}.json`), JSON.stringify(content, null, 2));
      };
      write("enabled", (entry) => entry.status === "passed" && entry.mockProviderEvidence);
      write("disabled", (entry) => entry.status === "skipped" && entry.disabled);
    }
  }
}
