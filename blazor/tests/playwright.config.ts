/// <reference types="node" />
import { defineConfig } from "@playwright/test";

import baseConfig from "../../application/shared-webapp/tests/e2e/playwright.config";
import { getBaseUrl } from "../../application/shared-webapp/tests/e2e/utils/constants";
import { blazorPath } from "./e2e/support/routes";
import { blazorLocales } from "./e2e/support/texts";

/**
 * The shared projects (each browser once for @smoke and once for everything else) run once per culture the Blazor edition
 * ships. A project is named browser-culture-lane, for example "chromium-da-DK-smoke", so every name is unique and the
 * developer CLI selects both cultures and both lanes of a browser with "--project=chromium-*". Tests run fully parallel;
 * nothing depends on the order of the projects.
 */
/**
 * Chromium fetches a service worker script outside the context, so ignoreHTTPSErrors does not cover it and the offline
 * shell's worker is refused over the development certificate with "An SSL certificate error occurred when fetching the
 * script". The browser is told to accept that certificate as well, which is the same allowance the shared configuration
 * already makes for every other request of these local runs. Only the Blazor projects are affected.
 */
const chromiumCertificate = { launchOptions: { ...baseConfig.use?.launchOptions, args: ["--ignore-certificate-errors"] } };

const cultureProjects = baseConfig.projects!.flatMap((project) =>
  blazorLocales.map((locale) => ({
    ...project,
    name: `${project.name}-${locale}-${project.grep ? "smoke" : "comprehensive"}`,
    use: { ...project.use, locale, ...(project.name.startsWith("chromium") ? chromiumCertificate : {}) }
  }))
);

const baseReporters = typeof baseConfig.reporter === "string" ? [[baseConfig.reporter] as [string]] : (baseConfig.reporter ?? []);

/**
 * The shared configuration (retries, timeouts, reporters) with the Blazor host's path base as the base URL and the culture
 * projects. The shared output and report folders are relative, so they resolve under blazor/tests/test-results. The
 * external login reporter adds the provider-enabled and provider-disabled result files the strict verdict reads. See
 * https://playwright.dev/docs/test-configuration.
 */
export default defineConfig({
  ...baseConfig,
  reporter: [...baseReporters, ["./e2e/support/external-login-reporter.ts"]],
  testDir: "./e2e",
  testMatch: "**/*.spec.ts",
  use: {
    ...baseConfig.use,
    baseURL: `${getBaseUrl()}${blazorPath()}`,
    storageState: undefined
  },
  projects: cultureProjects
});
