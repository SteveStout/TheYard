/// <reference types="vitest/config" />
import { build, defineConfig, type Plugin } from 'vite';
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

// #region draw-the-landing-page
// The landing page drawn to HTML when the site is built (ADR: The landing page
// rendered at build time, server rendering as the goal). Until this, the HTML a
// browser received was an empty frame, and a phone showed nothing until the
// script had arrived and run. Now the bare address arrives with the landing page
// already in #root, so the browser paints it as soon as the stylesheet is in,
// and src/app/mount.tsx takes it over with hydrateRoot rather than drawing it
// again from nothing.
//
// How: before the bundler starts on the site, a server-side build of
// src/app/drawLanding.tsx is made in memory from the same sources and the same
// stylesheet rules, so every class name it writes is the class name the
// browser's bundle uses. Everything it needs is bundled into one module (React's
// edge renderer included, so it reads no file and no Node API), that module
// is run once, and the markup it returns is written into the page when the page
// is written. It runs first, and not from inside the bundler's own hooks,
// because a second build started from inside the first one never finished. A
// draw that takes longer than a minute fails the build rather than holding it.
// Nothing is written to disk but index.html, and the API serves that file as it
// serves every other: it knows nothing about how it was made.
//
// The development server has no bundle, so there the page stays empty and the
// browser draws everything, as before.
const DRAW_TIMEOUT_MS = 60_000;
// The timer functions, as Node gives them; this config is typed without Node's types (see font-preload above).
const timers = globalThis as unknown as {
  setTimeout: (run: () => void, ms: number) => unknown;
  clearTimeout: (id: unknown) => void;
};

async function drawLandingMarkup(root: string): Promise<string> {
  const built = await build({
    configFile: false,
    root,
    logLevel: 'warn',
    plugins: [react()],
    ssr: { noExternal: true },
    build: {
      ssr: 'src/app/drawLanding.tsx',
      write: false,
      minify: false,
      rolldownOptions: { output: { codeSplitting: false } },
    },
  });
  const outputs = Array.isArray(built) ? built : 'output' in built ? [built] : [];
  const entry = outputs
    .flatMap((output) => output.output)
    .find((chunk) => chunk.type === 'chunk' && chunk.isEntry);
  if (!entry || entry.type !== 'chunk') {
    throw new Error('The landing page was not built for drawing');
  }
  const drawing: { drawLanding: () => string } = await import(
    /* @vite-ignore */ `data:text/javascript;charset=utf-8,${encodeURIComponent(entry.code)}`
  );
  return drawing.drawLanding();
}

function drawTheLandingPage(): Plugin {
  let markup = '';
  return {
    name: 'theyard-draw-the-landing-page',
    apply: 'build',
    async configResolved(config) {
      // A build of the server-side module itself carries the ssr flag: nothing to draw there.
      if (config.build.ssr) return;
      let timer: unknown;
      const late = new Promise<never>((_, reject) => {
        timer = timers.setTimeout(
          () => reject(new Error('Drawing the landing page took over a minute')),
          DRAW_TIMEOUT_MS
        );
      });
      try {
        markup = await Promise.race([drawLandingMarkup(config.root), late]);
      } finally {
        timers.clearTimeout(timer);
      }
    },
    transformIndexHtml: {
      order: 'post',
      handler(html, context) {
        // Only the built page; the development server draws in the browser.
        if (!context.bundle) return html;
        const empty = '<div id="root"></div>';
        if (!html.includes(empty)) throw new Error('index.html has no empty #root to draw into');
        return html.replace(empty, `<div id="root" data-drawn="landing">${markup}</div>`);
      },
    },
  };
}
// #endregion draw-the-landing-page

// #region dev-server
// The .NET API (api/) owns /api: data and vehicle photos. Proxying keeps the
// browser same-origin, so the API needs no CORS configuration. The preview
// server needs the same proxy or `npm run preview` breaks.
const apiProxy = {
  '/api': 'http://localhost:5210',
  // The page about him is the API's too (api/TheYard.Api/AboutPage.cs), outside /api.
  '/about': 'http://localhost:5210',
};

export default defineConfig({
  plugins: [react(), preloadTheFonts(), drawTheLandingPage()],
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
    // Unit tests only; tests/e2e belongs to Playwright. render/ holds the rendering service's own.
    include: ['src/**/*.test.ts', 'render/**/*.test.ts'],
    // allowOnly is deliberately not set. Vitest already defaults it to
    // !process.env.CI, which is the rule this project wants: a committed
    // `it.only` turns a suite into one test and still reports green, so CI
    // refuses one while a developer debugging a single test can focus it.
    // Writing the default out would need @types/node in a project that has
    // never needed it, for one boolean. What is checked instead is that
    // nothing here turns it off (ADR: Broken windows, and the rule that
    // answers them).
    // Vitest blanks CSS imports it is not told to process. These are read as
    // text: the four token sheets (through src/lib/styleSheet.ts, for the
    // contrast tests and the swatches), the panels sheet (panels.test.ts), the
    // ribbon sheet (ribbons.test.ts) and every component sheet (icons.test.ts,
    // for a stroke written as a number of its own).
    css: {
      include: [
        /(colors|sizes|typography|effects)\.css\?raw$/,
        /panels\.css\?raw$/,
        /Ribbons\.module\.css\?raw$/,
        /(components|library)\/.+\.module\.css\?raw$/,
      ],
    },
  },
  // #endregion unit-tests
});
