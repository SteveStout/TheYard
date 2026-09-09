// Renders infrastructure.svg to infrastructure.png at 2x in Chrome, the way
// dataflow.mjs renders its own drawing, for the hand-drawn picture that has no
// generator. Run from the repo root:
//   node docs/images/render.mjs             writes docs/images/infrastructure.png
//   node docs/images/render.mjs <in> <out>  any SVG to any PNG
// The site's font is fetched for the render so the picture on the README is
// the drawing and not a second drawing of it (ADR-006 addendum).
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const [input = join(here, 'infrastructure.svg'), output = join(here, 'infrastructure.png')] =
  process.argv.slice(2);
const svg = readFileSync(input, 'utf8');
const width = Number(/width="(\d+)"/.exec(svg)[1]);
const height = Number(/height="(\d+)"/.exec(svg)[1]);

const { chromium } = await import('@playwright/test');
const browser = await chromium.launch({ channel: 'chrome' });
try {
  const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 2 });
  await page.setContent(
    `<!doctype html><html><head>
      <link href="https://fonts.googleapis.com/css2?family=Poppins:wght@400;500;600;700&display=swap" rel="stylesheet">
      <style>html,body{margin:0;background:#e9e6e7}svg{display:block}</style>
    </head><body>${svg}</body></html>`,
    { waitUntil: 'networkidle' }
  );
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  await page.locator('svg').screenshot({ path: output, type: 'png' });
  console.log(`wrote ${output} (${width} by ${height}, at 2x)`);
} finally {
  await browser.close();
}
