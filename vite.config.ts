/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// #region dev-server
// The .NET API (api/) owns /api: data and vehicle photos. Proxying keeps the
// browser same-origin, so the API needs no CORS configuration. The preview
// server needs the same proxy or `npm run preview` breaks.
const apiProxy = {
  '/api': 'http://localhost:5210',
};

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: apiProxy,
    // Keep Vite's file watcher out of the .NET build output, because dotnet holds
    // locks on those files, which crashes the watcher on Windows (EBUSY).
    watch: {
      ignored: ['**/api/**'],
    },
  },
  preview: {
    proxy: apiProxy,
  },
  // #endregion dev-server
  // #region unit-tests
  // Vitest reads its settings from the same file as the dev server, which is
  // why there is no vitest.config.ts. The include pattern keeps it to the unit
  // tests, and the one CSS entry exists because Vitest blanks CSS imports it
  // is not told to process, which would leave the palette test with nothing to
  // measure.
  test: {
    // Unit tests only; tests/e2e belongs to Playwright.
    include: ['src/**/*.test.ts'],
    // allowOnly is deliberately not set. Vitest already defaults it to
    // !process.env.CI, which is the rule this project wants: a committed
    // `it.only` turns a suite into one test and still reports green, so CI
    // refuses one while a developer debugging a single test can focus it.
    // Writing the default out would need @types/node in a project that has
    // never needed it, for one boolean. What is checked instead is that
    // nothing here turns it off (ADR: Broken windows, and the rule that
    // answers them).
    // Vitest blanks CSS imports it is not told to process. tokens.test.ts reads
    // the palette file raw to measure its contrast, so that one goes through.
    css: { include: [/tokens\.css\?raw$/] },
  },
  // #endregion unit-tests
});
