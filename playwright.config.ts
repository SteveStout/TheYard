import { defineConfig } from '@playwright/test';

/**
 * End-to-end smokes against the real stack. Playwright launches both servers
 * itself (and reuses ones already running, so `npm run test:e2e` works while
 * you're developing). Uses the locally installed Chrome; in CI, install the
 * chrome channel first: npx playwright install --with-deps chrome
 */
export default defineConfig({
  testDir: 'tests/e2e',
  timeout: 30_000,
  fullyParallel: false,
  // #region reporters
  // A list on the console and an HTML report on disk.
  //
  // The HTML report is the reason this line exists. CI has an upload-artifact
  // step for `playwright-report/` and it has never uploaded anything, because
  // without an html reporter that directory is never written: every failing run
  // said "No files were found with the provided path". A failure on a runner
  // nobody can log into is only useful if it leaves evidence behind, and this
  // is the evidence.
  reporter: [['list'], ['html', { open: 'never' }]],
  // #endregion reporters
  // #region warm-up
  // The first page load of a run pays for Vite's first compile and the API's
  // first query. Paid once here, where a slow answer is expected, rather than
  // inside whichever spec happens to go first.
  globalSetup: './tests/e2e/warm-up.ts',
  // #endregion warm-up
  use: {
    baseURL: 'http://localhost:5173',
    channel: 'chrome',
  },
  // #region web-servers
  // Playwright starts both servers itself, so `npm run test:e2e` needs nothing
  // running first, and reuses ones already up, so it also works mid-development.
  // Each server is considered ready when a real URL answers, not after a sleep.
  webServer: [
    {
      command: 'npm run api',
      // The simulated room waits twenty seconds before answering a bid
      // (ADR-027). Zero here so the outbid test watches a lead change hands
      // instead of watching a clock. Nothing else sets this.
      env: { Market__GraceSeconds: '0' },
      url: 'http://localhost:5210/api/facets',
      reuseExistingServer: true,
      timeout: 120_000,
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: true,
      timeout: 120_000,
    },
  ],
  // #endregion web-servers
});
