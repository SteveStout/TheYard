/// <reference types="vitest/config" />
import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// #region font-preload
// The type file (IBM Plex Sans since 1.0.3.19, one variable file for its four
// weights; the four Poppins files before it) is named in src/styles/fonts.css,
// which the browser only reads after it has fetched the stylesheet, so on a
// cold visit the type is discovered one round trip late: measured on the live
// sites on 2026-09-22, the Poppins files started about 150 ms after the CSS and
// each took another 140 to 165 ms. A preload link in the head starts it with
// the stylesheet instead. It is 29 KB and every weight in it paints on the
// first screen (body, medium, semibold and bold), so none of this is
// speculative.
//
// The name is read from the build's own output rather than written here,
// because Vite hashes it. The dev server has no bundle, so there the source
// path is what the page asks for; it is written out rather than read off the
// disk because this project carries no @types/node and is not adding it for
// one string. A drift between this and the file is caught by fonts.spec.ts,
// which counts the links and reads the face the page actually painted with.
const FACES = ['ibm-plex-sans-latin'];

function preloadTheFonts(): Plugin {
  return {
    name: 'theyard-preload-fonts',
    transformIndexHtml: {
      order: 'post',
      handler(html, context) {
        const built = Object.keys(context.bundle ?? {}).filter((file: string) =>
          file.endsWith('.woff2')
        );
        const hrefs: string[] =
          built.length > 0
            ? built.map((file: string) => `/${file}`)
            : FACES.map((face) => `/src/assets/fonts/${face}.woff2`);
        return {
          html,
          tags: hrefs.sort().map((href) => ({
            tag: 'link',
            attrs: { rel: 'preload', as: 'font', type: 'font/woff2', href, crossorigin: '' },
            injectTo: 'head-prepend' as const,
          })),
        };
      },
    },
  };
}
// #endregion font-preload

// #region dev-server
// The .NET API (api/) owns /api: data and vehicle photos. Proxying keeps the
// browser same-origin, so the API needs no CORS configuration. The preview
// server needs the same proxy or `npm run preview` breaks.
const apiProxy = {
  '/api': 'http://localhost:5210',
};

export default defineConfig({
  plugins: [react(), preloadTheFonts()],
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
    // the palette file raw to measure its contrast, and ribbons.test.ts reads
    // the ribbon sheet raw to hold its performance rules, so those go through,
    // and from 1.0.3.10 icons.test.ts reads every component sheet raw for a
    // stroke written in a number of its own, so those go through as well, and
    // operator.test.ts reads the operator's look's shared sheet raw.
    css: {
      include: [
        /tokens\.css\?raw$/,
        /operator\.css\?raw$/,
        /Ribbons\.module\.css\?raw$/,
        /components\/[^/]+\.module\.css\?raw$/,
      ],
    },
  },
  // #endregion unit-tests
});
