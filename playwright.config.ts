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
