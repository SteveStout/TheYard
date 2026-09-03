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
// Run: node docs/images/og.mjs   (writes public/og.svg; the PNG is rendered
// from it, because most unfurlers will not fetch an SVG.)
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';

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
console.log(`wrote docs/images/og.svg with ${records} records, version ${version}`);
