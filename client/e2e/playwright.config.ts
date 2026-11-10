import { fileURLToPath } from 'node:url';
import { defineConfig } from '@playwright/test';
import { apiDir, apiLog, baseURL, usesRunningApi } from './paths';

/**
 * The end-to-end tests (README.md): the client the API serves, against the real API and a real SQL Server. Nothing is
 * mocked. They run one at a time: each signs up its own business, and the API is one process.
 */
const client = fileURLToPath(new URL('..', import.meta.url));

export default defineConfig({
  testDir: './specs',
  outputDir: './.work/results',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { outputFolder: './.work/report', open: 'never' }]],
  use: {
    baseURL,
    locale: 'es-MX',
    timezoneId: 'America/Mexico_City',
    viewport: { width: 1440, height: 900 },
    // A step that cannot happen fails in seconds, saying which, instead of waiting for the test's limit.
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    launchOptions: process.env.E2E_CHROMIUM ? { executablePath: process.env.E2E_CHROMIUM } : {},
  },
  webServer: usesRunningApi
    ? undefined
    : {
      command: `node e2e/start-api.mjs ${JSON.stringify(baseURL)} ${JSON.stringify(apiDir)} ${JSON.stringify(apiLog)}`,
      cwd: client,
      // /health/ready answers once the database migrated and answers: the first run migrates a new database.
      url: `${baseURL}/health/ready`,
      timeout: 180_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
    },
});
