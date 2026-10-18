// Draws onion-side-by-side.svg, the textbook onion beside TheYard's own rings, in the style of
// rings.svg: nested rounded boxes on each side, a tie line from each textbook ring to the project
// that is that ring here, and the one rule both sides obey. Run from the repo root:
//   node docs/images/onion-side-by-side.mjs          writes docs/images/onion-side-by-side.svg
//   node docs/images/onion-side-by-side.mjs --png    also renders the png at 2x in Chrome
// Redraw it when a ring gains or loses a project; the picture is a claim about the code.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;
const H = 1000;

const STYLE = `
      .ring0 { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; }
      .ring1 { fill: #ece9ea; stroke: #7b7f8a; stroke-width: 1.5; }
      .ring2 { fill: #f7f5f6; stroke: #7b7f8a; stroke-width: 1.5; }
      .ring3 { fill: #ffffff; stroke: #536786; stroke-width: 2; }
      .ring4 { fill: #f7f5f6; stroke: #536786; stroke-width: 2; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 700; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .tie { stroke: #4a6c96; stroke-width: 1.5; fill: none; stroke-dasharray: 5 4; opacity: 0.75; }
      .inward { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .flow-label { fill: #4a6c96; font-size: 12px; font-weight: 500; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .side-heading { fill: #3f3a37; font-size: 17px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const out = [];
const text = (x, y, cls, s) => out.push(`  <text x="${x}" y="${y}" class="${cls}">${esc(s)}</text>`);

/** One ring: a rounded box, the name of the ring, and what lives in it. */
function ring({ cls, x, y, w, h, name, project, lines = [] }) {
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" class="${cls}"/>`);
  text(x + 18, y + 26, 'title', name);
  let line = y + 44;
  if (project) {
    text(x + 18, line, 'mono', project);
    line += 18;
  }
  lines.forEach((words, i) => text(x + 18, line + 16 * i, 'body', words));
}

// The textbook onion, outermost first. Each ring's label sits at the top of its own band, so a tie
// line leaves it at that height.
const LEFT = [
  {
    cls: 'ring0',
    x: 40,
    y: 150,
    w: 560,
    h: 760,
    name: 'Frameworks, drivers, tests',
    lines: ['The web framework, the database', 'engine, the browser.'],
  },
  {
    cls: 'ring1',
    x: 78,
    y: 245,
    w: 484,
    h: 570,
    name: 'Interface adapters',
    lines: ['Controllers, gateways,', 'repositories: one real thing each.'],
  },
  {
    cls: 'ring2',
    x: 116,
    y: 340,
    w: 408,
    h: 380,
    name: 'Use cases, the application',
    lines: ['What the software does with the', 'rules, and the ports it needs.'],
  },
  {
    cls: 'ring3',
    x: 154,
    y: 435,
    w: 332,
    h: 190,
    name: 'Entities, the domain model',
    lines: ['The rules that would exist', 'with no software at all.'],
  },
];

// TheYard, ring for ring, with the centre split in two: a ring with no behaviour at all is a ring
// nothing can hide in.
const RIGHT = [
  {
    cls: 'ring0',
    x: 800,
    y: 150,
    w: 560,
    h: 760,
    name: 'Host, front end, tests',
    project: 'api/TheYard.Api, src/, render/, api/TheYard.Tests',
    lines: ['Endpoints read a request and ask Auction;', 'Composition/ wires every service.'],
  },
  {
    cls: 'ring1',
    x: 838,
    y: 245,
    w: 484,
    h: 570,
    name: 'The adapters, the same ports twice',
    project: 'api/TheYard.Infrastructure[.Cosmos]',
    lines: ['EF Core over Azure SQL or SQLite,', 'and the Cosmos DB SDK.'],
  },
  {
    cls: 'ring2',
    x: 876,
    y: 340,
    w: 408,
    h: 380,
    name: 'The use cases',
    project: 'api/TheYard.Application',
    lines: ['Auction, and the ports the auction', 'and the operator need.'],
  },
  {
    cls: 'ring3',
    x: 914,
    y: 435,
    w: 332,
    h: 190,
    name: 'The rules',
    project: 'api/TheYard.Domain',
    lines: ['BidRules, AuctionSchedule,', 'StandingRules, VehicleFilter.'],
  },
  {
    cls: 'ring4',
    x: 990,
    y: 530,
    w: 256,
    h: 104,
    name: 'The shapes',
    project: 'api/TheYard.Data',
    lines: ['Records with no behaviour.'],
  },
];

[...LEFT, ...RIGHT].forEach(ring);

// A tie from each textbook ring to the project that is that ring here, drawn at the height of the
// label it leaves. The centre's tie fans into two, which is the one deliberate difference.
const TIES = [
  [0, 0],
  [1, 1],
  [2, 2],
  [3, 3],
  [3, 4],
];
TIES.forEach(([from, to]) => {
  const a = LEFT[from];
  const b = RIGHT[to];
  const y1 = a.y + 20;
  const y2 = b.y + 20;
  const x1 = a.x + a.w;
  const x2 = b.x;
  out.push(`  <path d="M${x1} ${y1} C ${x1 + 70} ${y1}, ${x2 - 70} ${y2}, ${x2} ${y2}" class="tie"/>`);
});

// The rule both sides obey, drawn inside each onion: a line from the outermost ring to the edge of
// the innermost, which is the direction every dependency is allowed to run.
[48, 808].forEach((x) => out.push(`  <path d="M${x} 680 L${x + 98} 680" class="inward"/>`));
[40, 800].forEach((x) => text(x + 2, 712, 'flow-label', 'Dependencies point inward'));

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>Palermo's onion beside TheYard's rings: each textbook ring tied to the project that is that ring, every dependency pointing inward</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <style>${STYLE}    </style>
  </defs>

  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="40" y="48" class="heading">The textbook onion, and the one this codebase is built as</text>
  <text x="40" y="72" class="caption">Four rings on the left, as the pattern is written down. On the right, the projects that are those rings, one deliberate difference: the centre is split in two.</text>
  <text x="40" y="118" class="side-heading">The pattern (Cockburn 2005, Palermo 2008, Martin 2012)</text>
  <text x="800" y="118" class="side-heading">TheYard</text>

${out.join('\n')}

  <text x="40" y="${H - 50}" class="caption">A dashed line ties a textbook ring to the project that is that ring. Ten tests in api/TheYard.Tests/OnionTests.cs fail the build when a dependency points the other way.</text>
  <text x="40" y="${H - 28}" class="caption">Source: docs/images/onion-side-by-side.svg in the repository, drawn by docs/images/onion-side-by-side.mjs and redrawn when a ring changes.</text>
</svg>
`;

const svgPath = join(here, 'onion-side-by-side.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(`<!doctype html><html><head><style>html,body{margin:0;background:#e9e6e7}</style></head><body>${svg}</body></html>`);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'onion-side-by-side.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}
