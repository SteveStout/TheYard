// The link preview card, drawn rather than screenshotted.
//
// A screenshot of the running app was the obvious first idea and it is the
// wrong artefact: at 1200x630 in a chat client the inventory grid renders as
// grey confetti, and the one thing the card has to do is say what this is to
// somebody who has not clicked yet. So it is typeset, in the application's own
// palette, from the same tokens the site uses, and the numbers in it are read
// from the repository rather than typed here, so a card cannot claim a count
// the project no longer has.
//
// Run: node docs/images/og.mjs
//
// It writes both the SVG and the PNG, in that order, from one invocation. That
// is deliberate. The first version wrote the SVG here and rendered the PNG from
// a throwaway script kept somewhere else, and the two immediately drifted: the
// count in the drawing said forty-six and the picture every unfurler would
// actually fetch still said forty-five. Two artefacts that must agree should not
// have two commands.
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { chromium } from '@playwright/test';

const palette = {
  ink: '#5e5653',
  ground: '#e9e6e7',
  surface: '#ffffff',
  accent: '#536786',
  accentSoft: '#e4e9f1',
  muted: '#5f636c',
  sand: '#ab978c',
};

const records = readdirSync('docs').filter((name) => /^ADR-\d+/.test(name)).length;
const changelog = readFileSync('docs/CHANGELOG.md', 'utf8');
const version = changelog.match(/\*\*(\d+\.\d+\.\d+\.\d+)\*\*/)?.[1] ?? '';

const facts = [
  ['100,000', 'listings'],
  [String(records), 'decision records'],
  ['.NET 10', 'and React 19'],
];

const escape = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630" role="img" aria-label="TheYard, a used-vehicle auction platform, by Steven Stout">
  <rect width="1200" height="630" fill="${palette.ground}"/>
  <rect x="56" y="56" width="1088" height="518" rx="24" fill="${palette.surface}"/>
  <rect x="56" y="56" width="1088" height="10" rx="5" fill="${palette.accent}"/>

  <g transform="translate(112, 150)">
    <rect x="0" y="-46" width="64" height="64" rx="14" fill="${palette.ink}"/>
    <path d="M26 -36 L16 -10 h10 l-4 18 l20 -28 h-12 l8 -16 z" fill="${palette.sand}"/>
    <text x="88" y="6" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="58" font-weight="700" fill="${palette.ink}">TheYard</text>
  </g>

  <text x="112" y="264" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="40" font-weight="500" fill="${palette.ink}">A used-vehicle auction platform,</text>
  <text x="112" y="316" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="40" font-weight="500" fill="${palette.ink}">with its reasoning served from inside it.</text>

  <g font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif">
${facts
  .map(([value, label], index) => {
    const x = 112 + index * 336;
    return `    <rect x="${x}" y="374" width="304" height="108" rx="16" fill="${palette.accentSoft}"/>
    <text x="${x + 28}" y="424" font-size="38" font-weight="700" fill="${palette.accent}">${escape(value)}</text>
    <text x="${x + 28}" y="458" font-size="22" font-weight="500" fill="${palette.muted}">${escape(label)}</text>`;
  })
  .join('\n')}
  </g>

  <text x="112" y="534" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="24" font-weight="500" fill="${palette.muted}">theyard.stevenstout.biz</text>
  <text x="1088" y="534" text-anchor="end" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="24" font-weight="500" fill="${palette.muted}">Steven Stout${version ? ` · ${version}` : ''}</text>
</svg>
`;

writeFileSync('docs/images/og.svg', svg);

// The PNG is what a link preview actually fetches: an SVG og:image is ignored by
// most unfurlers. Rendered rather than converted, so the Poppins the site uses
// is the Poppins in the card.
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.setContent(
  `<!doctype html><html><head><meta charset="utf-8">
   <link rel="preconnect" href="https://fonts.googleapis.com">
   <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Poppins:wght@400;500;600;700&display=swap">
   <style>html,body{margin:0;padding:0;width:1200px;height:630px;overflow:hidden}</style>
   </head><body>${svg}</body></html>`,
  { waitUntil: 'networkidle' }
);
// The font has to have painted before the shutter, or the card ships in Arial.
await page.waitForTimeout(1200);
await page.screenshot({ path: 'public/og.png' });
await browser.close();

console.log(`wrote docs/images/og.svg and public/og.png with ${records} records, version ${version}`);
