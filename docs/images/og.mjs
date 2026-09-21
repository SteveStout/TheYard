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

// The teal, dark green and gold of the site since 1.0.0.169 (ADR: The palette,
// the addendum on teal, dark green and gold): the header's own gradient as the
// ground, top to bottom, the gold mark and the gold name on it, and white
// words. On a post this card is seen before the site is, about four hundred
// pixels wide, so the name is drawn large enough to read at that size and one
// line says what this is.
const palette = {
  top: '#0a3021',
  bottom: '#03505a',
  gold: '#d4aa3a',
  goldLight: '#dcbf57',
  white: '#ffffff',
  soft: '#cfe3e6',
  chip: '#024345',
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
  <defs>
    <linearGradient id="ground" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="${palette.top}"/>
      <stop offset="1" stop-color="${palette.bottom}"/>
    </linearGradient>
  </defs>
  <rect width="1200" height="630" fill="url(#ground)"/>
  <g fill="none" stroke="${palette.soft}" stroke-opacity="0.12" stroke-width="2">
    <circle cx="1010" cy="170" r="120"/><circle cx="1010" cy="170" r="210"/><circle cx="1010" cy="170" r="300"/><circle cx="1010" cy="170" r="390"/>
  </g>
  <rect x="0" y="618" width="1200" height="12" fill="${palette.goldLight}"/>

  <g transform="translate(96, 176)">
    <path d="M44 -84 L8 -8 h34 l-14 60 l70 -96 h-42 l28 -40 z" fill="${palette.goldLight}"/>
    <text x="124" y="16" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="104" font-weight="700" fill="${palette.goldLight}">TheYard</text>
  </g>

  <text x="96" y="296" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="44" font-weight="600" fill="${palette.white}">A working used-vehicle auction site,</text>
  <text x="96" y="352" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="44" font-weight="600" fill="${palette.white}">built and explained by Steven Stout.</text>

  <g font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif">
${facts
  .map(([value, label], index) => {
    const x = 96 + index * 344;
    return `    <rect x="${x}" y="404" width="320" height="112" rx="14" fill="${palette.chip}" stroke="${palette.soft}" stroke-opacity="0.35"/>
    <text x="${x + 28}" y="456" font-size="40" font-weight="700" fill="${palette.white}">${escape(value)}</text>
    <text x="${x + 28}" y="492" font-size="24" font-weight="500" fill="${palette.soft}">${escape(label)}</text>`;
  })
  .join('\n')}
  </g>

  <text x="96" y="580" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="26" font-weight="500" fill="${palette.soft}">theyard.stevenstout.biz</text>
  <text x="1104" y="580" text-anchor="end" font-family="Poppins, Segoe UI, system-ui, Arial, sans-serif" font-size="26" font-weight="500" fill="${palette.soft}">Steven Stout${version ? ` · ${version}` : ''}</text>
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
