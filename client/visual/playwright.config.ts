import { fileURLToPath } from 'node:url';
import { defineConfig } from '@playwright/test';

/**
 * The visual scenes (README.md). They run against a build of the client served by serve.mjs: `VISUAL_DIST` names it
 * (`dist` by default, the baseline's build when run.mjs takes it). Snapshots and reports stay out of git.
 */
const port = Number(process.env.VISUAL_PORT ?? 4317);
const client = fileURLToPath(new URL('..', import.meta.url));
const dist = process.env.VISUAL_DIST ?? 'dist';

export default defineConfig({
  testDir: './scenes',
  outputDir: './.work/results',
  snapshotPathTemplate: '{testDir}/../.snapshots/{testFilePath}/{arg}{ext}',
  fullyParallel: true,
  workers: process.env.VISUAL_WORKERS ? Number(process.env.VISUAL_WORKERS) : '50%',
  timeout: 60_000,
  reporter: [['list'], ['html', { outputFolder: './.work/report', open: 'never' }]],
  expect: {
    // Zero tolerance: in 20B to 20E the template changes underneath and nothing on screen may move.
    toHaveScreenshot: { animations: 'disabled', caret: 'hide', scale: 'css', threshold: 0, maxDiffPixels: 0 },
    toMatchSnapshot: { threshold: 0, maxDiffPixels: 0 },
  },
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    locale: 'es-MX',
    timezoneId: 'America/Mexico_City',
    colorScheme: 'light',
    viewport: { width: 1440, height: 900 },
    launchOptions: {
      ...(process.env.VISUAL_CHROMIUM ? { executablePath: process.env.VISUAL_CHROMIUM } : {}),
      // Whole tiles are repainted: after a change Chromium repaints only its part of a tile, and on a busy machine the
      // edges of that part (an opened menu, a field in a dialog) came out a shade apart from one shot to the next.
      args: ['--disable-partial-raster'],
    },
  },
  webServer: {
    // From client/: VISUAL_DIST is relative to it or absolute.
    command: `node visual/serve.mjs ${JSON.stringify(dist)} ${port}`,
    cwd: client,
    url: `http://127.0.0.1:${port}/`,
    reuseExistingServer: false,
    stdout: 'ignore',
  },
});
