import { defineConfig } from '@playwright/test';

/**
 * End-to-end smokes against the real stack. Playwright launches both servers
 * itself (and reuses ones already running, so `npm run test:e2e` works while
 * you're developing). Uses the locally installed Chrome — in CI, install the
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
  webServer: [
    {
      command: 'npm run api',
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
});
