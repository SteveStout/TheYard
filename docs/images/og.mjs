// The link preview card, drawn rather than screenshotted.
//
// A screenshot of the running app was the obvious first idea and it is the
// wrong artefact: at 1200x630 in a chat client the inventory grid renders as
// grey confetti, and the one thing the card has to do is say what this is to
// somebody who has not clicked yet. So it is typeset, in the application's own
// palette, from the same tokens the site uses, and the numbers in it are read
// from the repository rather than typed here, so a card cannot claim a number
// the site no longer shows.
//
// Run: node docs/images/og.mjs
//
// It writes both the SVG and the PNG, in that order, from one invocation. That
// is deliberate. The first version wrote the SVG here and rendered the PNG from
// a throwaway script kept somewhere else, and the two immediately drifted: the
// count in the drawing said forty-six and the picture every unfurler would
// actually fetch still said forty-five. Two artefacts that must agree should not
// have two commands.
import { readFileSync, writeFileSync } from 'node:fs';
import { chromium } from '@playwright/test';

// Person first (ADR: The public face, the addendum on the person-first card): the card every page a recruiter is
// sent shares, in the teal and gold of the site (ADR: The palette), his photo in a gold ring, his name and role,
// the review route's headline, and three figures, each read from the file that already holds it so the card can
// never say a number the site does not. No version and no record count: the card is the person, and a card that
// changed at every landing was a card nobody looked at twice. A blog post keeps its own image (ADR-132).
const palette = {
  top: '#125568',
  bottom: '#0f4a5a',
  gold: '#e6cb7e',
  goldDeep: '#7d582e',
  white: '#ffffff',
};

/** The review route's head title, the headline here, cut into its two sentences. */
const heads = readFileSync('render/heads.ts', 'utf8');
const headline = /const REVIEW: Head = \{\s*title: '([^']+)'/.exec(heads)?.[1] ?? '';
const [lineOne, lineTwo] = headline.split(/(?<=\.)\s+(?=[A-Z])/);

/** A record tile's figure from src/lib/reviewVerdict.ts, by its key. */
const verdict = readFileSync('src/lib/reviewVerdict.ts', 'utf8');
const figureOf = (key) => new RegExp(`key: '${key}',\\s*figure: '([^']+)'`).exec(verdict)?.[1] ?? '';

const recommendations = JSON.parse(readFileSync('data/recommendations.json', 'utf8')).recommendations.length;

const facts = [
  ['releases', figureOf('releases'), 'releases, zero downtime'],
  ['cycle', figureOf('cycle'), 'shorter release cycle'],
  ['recommendations', String(recommendations), 'named recommendations'],
];
if (!lineOne || !lineTwo || facts.some(([, value]) => !value)) {
  throw new Error(`the card could not read its words: ${JSON.stringify({ headline, facts })}`);
}

/** His photo, set into the drawing itself so the SVG stands alone. */
const photo = `data:image/webp;base64,${readFileSync('api/TheYard.Api/wwwroot/images/badges/steve-264.webp').toString('base64')}`;

const escape = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const font = 'IBM Plex Sans, Segoe UI, system-ui, Arial, sans-serif';
const alt =
  'Steven Stout, Lead / Staff .NET engineer, remote. Production .NET that stays up. Built with AI, measured on every version.';

// The measurements are the qa1126 mock's (J:\mentor\qa1126\mock-og-qa1126.html): 72 px in, 64 px down, a 168 px
// photo with a 5 px ring, the words 40 px beside it, three boxes 338.66 px wide with 20 px between them.
const box = (338.66 + 20).toFixed(2);
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630" role="img" aria-label="${escape(alt)}">
  <defs>
    <linearGradient id="ground" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="${palette.top}"/>
      <stop offset="1" stop-color="${palette.bottom}"/>
    </linearGradient>
    <linearGradient id="rule" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="${palette.goldDeep}"/>
      <stop offset="0.5" stop-color="${palette.gold}"/>
      <stop offset="1" stop-color="${palette.goldDeep}"/>
    </linearGradient>
    <clipPath id="face"><circle cx="156" cy="148" r="79"/></clipPath>
  </defs>
  <rect width="1200" height="630" fill="url(#ground)"/>
  <g fill="none" stroke="${palette.gold}" stroke-opacity="0.18">
    <circle cx="1030" cy="210" r="120"/><circle cx="1030" cy="210" r="200"/><circle cx="1030" cy="210" r="280"/><circle cx="1030" cy="210" r="345"/>
  </g>
  <rect x="0" y="618" width="1200" height="12" fill="url(#rule)"/>

  <image href="${photo}" x="77" y="69" width="158" height="158" preserveAspectRatio="xMidYMid slice" clip-path="url(#face)"/>
  <circle cx="156" cy="148" r="81.5" fill="none" stroke="${palette.gold}" stroke-width="5"/>

  <g font-family="${font}">
    <text x="280" y="143.5" font-size="64" font-weight="700" letter-spacing="-0.5" fill="${palette.white}">Steven Stout</text>
    <text x="280" y="197.8" font-size="30" font-weight="500" fill="${palette.gold}">Lead / Staff .NET engineer · Remote</text>
    <text x="72" y="310" font-size="38" font-weight="600" fill="${palette.white}">${escape(lineOne)}</text>
    <text x="72" y="357.5" font-size="38" font-weight="600" fill="${palette.white}">${escape(lineTwo)}</text>
${facts
  .map(([key, value, label], index) => {
    const x = (72 + index * Number(box)).toFixed(2);
    return `    <rect x="${(Number(x) + 0.5).toFixed(2)}" y="403.5" width="337.66" height="121.39" rx="18" fill="none" stroke="${palette.gold}" stroke-opacity="0.55"/>
    <text id="figure-${key}" x="${(Number(x) + 21).toFixed(2)}" y="460.1" font-size="44" font-weight="700" fill="${palette.white}">${escape(value)}</text>
    <text x="${(Number(x) + 21).toFixed(2)}" y="499" font-size="24" font-weight="400" fill="${palette.white}" fill-opacity="0.92">${escape(label)}</text>`;
  })
  .join('\n')}
  </g>
</svg>
`;

writeFileSync('docs/images/og.svg', svg);

// The PNG is what a link preview actually fetches: an SVG og:image is ignored by
// most unfurlers. Rendered rather than converted, so the IBM Plex Sans the site uses
// is the IBM Plex Sans in the card.
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.setContent(
  `<!doctype html><html><head><meta charset="utf-8">
   <link rel="preconnect" href="https://fonts.googleapis.com">
   <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&display=swap">
   <style>html,body{margin:0;padding:0;width:1200px;height:630px;overflow:hidden}</style>
   </head><body>${svg}</body></html>`,
  { waitUntil: 'networkidle' }
);
// The font has to have painted before the shutter, or the card ships in Arial.
await page.waitForTimeout(1200);
await page.screenshot({ path: 'public/og.png' });
await browser.close();

console.log(`wrote docs/images/og.svg and public/og.png: ${facts.map(([, value, label]) => `${value} ${label}`).join(', ')}`);
