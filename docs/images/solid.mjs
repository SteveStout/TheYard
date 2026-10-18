// Draws solid.svg: the five SOLID letters one under the next, each with what it means in plain
// words and where TheYard shows it, in the site's teal, green and gold. It is the picture at the
// top of the SOLID Principles page. Run from the repo root:
//   node docs/images/solid.mjs          writes docs/images/solid.svg
//   node docs/images/solid.mjs --png    also renders solid.png at 2x in Chrome
// Redraw it when the page's examples change; every line under "In TheYard" is a claim about the code.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

// Tall and narrow on purpose: the page previews it at the width of a phone, so each row is read
// top to bottom rather than five columns squeezed side by side.
const W = 900;
const TOP = 132;
const BAND = 156;
const TEXT_X = BAND + 44;
const TEXT_W = W - TEXT_X - 40;

const STYLE = `
      .ground { fill: #f4f2f3; }
      .card { fill: #ffffff; stroke: #d8d3d4; stroke-width: 1.5; }
      .heading { fill: #10495a; font-size: 40px; font-weight: 800; }
      .caption { fill: #5e5653; font-size: 21px; }
      .letter { fill: #ffffff; font-size: 92px; font-weight: 800; }
      .name { font-size: 29px; font-weight: 800; }
      .plain { fill: #332f2c; font-size: 22px; }
      .yard-label { font-size: 19px; font-weight: 700; }
      .yard { fill: #5e5653; font-size: 19px; }
      .icon { fill: none; stroke: #ffffff; stroke-width: 3.5; stroke-linecap: round; stroke-linejoin: round; }
      .icon-fill { fill: #ffffff; }
`;

// Deep green through teal to gold, each one the site's own colour and each dark enough to carry
// white text at the size it is drawn.
const ROWS = [
  {
    letter: 'S',
    name: 'Single Responsibility',
    colour: '#0f4452',
    plain: 'Each class has one job, so it has only one reason to change.',
    yard: 'A bidding rule lives once, in Domain, and Program.cs is a short list with one call per part.',
    icon: 'target',
  },
  {
    letter: 'O',
    name: 'Open / Closed',
    colour: '#12677f',
    plain: 'Add new behaviour by writing new code. Leave the code that already works alone.',
    yard: 'Cosmos DB arrived as new adapter classes behind the same ports the SQL store uses.',
    icon: 'add',
  },
  {
    letter: 'L',
    name: 'Liskov Substitution',
    colour: '#1f7894',
    plain: 'Anything that stands in for something else has to behave the same way.',
    yard: 'The same test suite runs against both stores, and a change that touches either store has to pass on both.',
    icon: 'swap',
  },
  {
    letter: 'I',
    name: 'Interface Segregation',
    colour: '#7d6326',
    plain: 'Keep interfaces small and shaped to the code that calls them.',
    yard: 'The auction uses three small ports, not one repository that does everything.',
    icon: 'split',
  },
  {
    letter: 'D',
    name: 'Dependency Inversion',
    colour: '#7d582e',
    plain: 'Important code depends on contracts it owns, never on the database underneath it.',
    yard: 'Application declares IBidStore; each store implements it, and startup wires the one in use.',
    icon: 'plug',
  },
];

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Breaks a sentence into lines of at most `max` characters, at spaces. */
function wrap(sentence, max) {
  const lines = [];
  let line = '';
  for (const word of sentence.split(' ')) {
    if (line && (line + ' ' + word).length > max) {
      lines.push(line);
      line = word;
    } else {
      line = line ? `${line} ${word}` : word;
    }
  }
  if (line) lines.push(line);
  return lines;
}

/** A small white drawing for each letter, centred on (x, y), about 64 px across. */
function icon(kind, x, y) {
  const g = (body) => `  <g transform="translate(${x - 32} ${y - 32})">${body}</g>`;
  switch (kind) {
    // One job: a target with one dot in the middle.
    case 'target':
      return g(`<circle cx="32" cy="32" r="28" class="icon"/><circle cx="32" cy="32" r="16" class="icon"/><circle cx="32" cy="32" r="5" class="icon-fill"/>`);
    // Extend without editing: a finished block, and a new one clicked on beside it.
    case 'add':
      return g(`<rect x="4" y="16" width="30" height="32" rx="4" class="icon"/><rect x="38" y="16" width="22" height="32" rx="4" class="icon" stroke-dasharray="5 4"/><path d="M49 26 V38 M43 32 H55" class="icon"/>`);
    // Substitution: two pieces of the same shape trading places.
    case 'swap':
      return g(`<rect x="4" y="6" width="24" height="20" rx="4" class="icon"/><rect x="36" y="38" width="24" height="20" rx="4" class="icon"/><path d="M34 16 H54 L48 10 M54 16 L48 22" class="icon"/><path d="M30 48 H10 L16 42 M10 48 L16 54" class="icon"/>`);
    // Small interfaces: one caller, three narrow sockets rather than one wide one.
    case 'split':
      return g(`<circle cx="32" cy="10" r="7" class="icon"/><path d="M32 17 V28 M10 28 H54 M10 28 V40 M32 28 V40 M54 28 V40" class="icon"/><rect x="2" y="40" width="16" height="18" rx="3" class="icon"/><rect x="24" y="40" width="16" height="18" rx="3" class="icon"/><rect x="46" y="40" width="16" height="18" rx="3" class="icon"/>`);
    // Depend on the contract: a plug meeting a socket, the arrow pointing at the socket.
    default:
      return g(`<path d="M4 32 H22" class="icon"/><rect x="22" y="20" width="14" height="24" rx="3" class="icon"/><path d="M36 26 H44 M36 38 H44" class="icon"/><path d="M50 14 V50 M50 14 H60 M50 50 H60" class="icon"/>`);
  }
}

const out = [];
const text = (x, y, cls, s, fill) =>
  out.push(`  <text x="${x}" y="${y}" class="${cls}"${fill ? ` fill="${fill}"` : ''}>${esc(s)}</text>`);

text(40, 64, 'heading', 'SOLID, in plain words');
text(40, 100, 'caption', 'Five habits that let code change without breaking.');

let y = TOP;
for (const row of ROWS) {
  const plain = wrap(row.plain, 56);
  const yard = wrap(row.yard, 64);
  const h = Math.max(206, 62 + plain.length * 30 + 22 + yard.length * 26 + 22);
  out.push(`  <rect x="20" y="${y}" width="${W - 40}" height="${h}" rx="18" class="card"/>`);
  // The coloured band holds the letter and its drawing; its right corners are squared by a second
  // rectangle so only the card's outer corners are round.
  out.push(`  <path d="M38 ${y} H${20 + BAND} V${y + h} H38 A18 18 0 0 1 20 ${y + h - 18} V${y + 18} A18 18 0 0 1 38 ${y} Z" fill="${row.colour}"/>`);
  out.push(`  <text x="${20 + BAND / 2}" y="${y + 100}" text-anchor="middle" class="letter">${row.letter}</text>`);
  out.push(icon(row.icon, 20 + BAND / 2, y + 150));

  let ty = y + 48;
  text(TEXT_X, ty, 'name', row.name, row.colour);
  ty += 38;
  for (const line of plain) {
    text(TEXT_X, ty, 'plain', line);
    ty += 30;
  }
  ty += 10;
  text(TEXT_X, ty, 'yard-label', 'In TheYard', row.colour);
  ty += 26;
  for (const line of yard) {
    text(TEXT_X, ty, 'yard', line);
    ty += 26;
  }
  y += h + 18;
}
const H = y + 8;

const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}" font-family="'Segoe UI', 'Helvetica Neue', Arial, sans-serif" role="img" aria-labelledby="solid-title">
  <title id="solid-title">SOLID in plain words: Single Responsibility, Open / Closed, Liskov Substitution, Interface Segregation and Dependency Inversion, each with where TheYard shows it</title>
  <style>${STYLE}  </style>
  <rect width="${W}" height="${H}" class="ground"/>
${out.join('\n')}
</svg>
`;

const svgPath = join(here, 'solid.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);
// The text widths above are set for a 900 px drawing; TEXT_W is the room each line has.
if (TEXT_W < 600) throw new Error('the text column is too narrow for the wrap widths');

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(`<!doctype html><html><head><style>html,body{margin:0;background:#f4f2f3}</style></head><body>${svg}</body></html>`);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'solid.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}
