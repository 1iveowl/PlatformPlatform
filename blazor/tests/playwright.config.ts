/// <reference types="node" />
import { defineConfig } from "@playwright/test";

import baseConfig from "../../application/shared-webapp/tests/e2e/playwright.config";
import { getBaseUrl } from "../../application/shared-webapp/tests/e2e/utils/constants";
import { blazorPath } from "./e2e/support/routes";

/**
 * The shared configuration (retries, timeouts, reporters, browsers split into @smoke and non-smoke projects) with the
 * Blazor host's path base as the base URL. The shared output and report folders are relative, so they resolve under
 * blazor/tests/test-results. See https://playwright.dev/docs/test-configuration.
 */
export default defineConfig({
  ...baseConfig,
  testDir: "./e2e",
  testMatch: "**/*.spec.ts",
  use: {
    ...baseConfig.use,
    baseURL: `${getBaseUrl()}${blazorPath()}`,
    // Headless Chromium in the container otherwise reports "en-US@posix", which the .NET WebAssembly runtime rejects as
    // a culture name
    locale: "en-US",
    storageState: undefined
  }
});
