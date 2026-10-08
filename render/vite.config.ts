// The rendering service's build (ADR: A rendering service beside the API). It is
// the site's own config with three changes, so every class name and asset address
// the service writes is the one the browser's bundle uses: the build is for a
// server, it starts from render/entry.ts, and it carries every package it needs
// inside it, so the image holds no node_modules. It runs as its own command after
// the site's build (npm run build:render), never from inside it (ADR: The landing
// page rendered at build time, on the build that never finished).
import { defineConfig, mergeConfig } from 'vite';
import site from '../vite.config.ts';

export default mergeConfig(
  site,
  defineConfig({
    ssr: { noExternal: true },
    build: {
      ssr: 'render/entry.ts',
      outDir: 'dist-render',
      emptyOutDir: true,
      copyPublicDir: false,
    },
  })
);
