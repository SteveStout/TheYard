/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The .NET API (api/) owns /api — data and vehicle photos. Proxying keeps the
// browser same-origin, so the API needs no CORS configuration. The preview
// server needs the same proxy or `npm run preview` breaks.
const apiProxy = {
  '/api': 'http://localhost:5210',
};

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: apiProxy,
    // Keep Vite's file watcher out of the .NET build output — dotnet holds
    // locks on those files, which crashes the watcher on Windows (EBUSY).
    watch: {
      ignored: ['**/api/**'],
    },
  },
  preview: {
    proxy: apiProxy,
  },
  test: {
    // Unit tests only — tests/e2e belongs to Playwright.
    include: ['src/**/*.test.ts'],
  },
});
