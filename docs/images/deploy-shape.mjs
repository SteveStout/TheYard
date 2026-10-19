// Draws deploy-shape.svg (ADR: One web app for every .NET process): one shape for every
// app that runs, read from the two workflows. Each is a Docker image built from the repository's own Dockerfile,
// pushed to the registry, set as the web app's image by tag, and verified before the next step. Run from the repo root:
//   node docs/images/deploy-shape.mjs     writes docs/images/deploy-shape.svg
// Redraw it when a workflow gains or loses a step; the picture is a claim.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const out = [];

/** One step box: a title, a monospace line and up to three body lines. */
function step(x, y, w, title, mono, lines) {
  const h = 128;
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" class="box"/>`);
  out.push(`  <text x="${x + 14}" y="${y + 26}" class="title">${esc(title)}</text>`);
  out.push(`  <text x="${x + 14}" y="${y + 47}" class="mono">${esc(mono)}</text>`);
  lines.forEach((line, i) => out.push(`  <text x="${x + 14}" y="${y + 70 + 17 * i}" class="body">${esc(line)}</text>`));
  return { x, y, w, h, right: x + w, midY: y + h / 2 };
}

const rows = [
  {
    name: 'The one web app: both sites and The Shed',
    workflow: '.github/workflows/deploy.yml',
    steps: [
      ['Build the image', 'Dockerfile, three stages', ['Node builds the page; the SDK', 'publishes TheYard and The Shed;', 'the runtime image holds both.']],
      ['Push to the registry', 'crtheyardsszmnetj67bn5h2/theyard:vN', ['One tag per version, signed in', 'as the workflow (OIDC), no', 'password anywhere.']],
      ['Point the web app at the tag', 'APP-THEYARD-ONE-SS: DOCKER|...:vN', ['The settings it holds, then the', 'image; App Service pulls it as', 'id-theyard-ss.']],
      ['Verify, then the edge', 'origin, the three names, domains', ['The version, readiness, the Cosmos', 'DB name and The Shed\'s name; the', 'edge moves after, if it changed.']],
    ],
  },
  {
    name: 'The rendering service',
    workflow: '.github/workflows/deploy-render.yml',
    steps: [
      ['Build the image', 'render/Dockerfile, two stages', ['Node builds the renderer from', 'the site\'s own source; the', 'runtime is Node and that build.']],
      ['Push to the registry', 'crtheyardsszmnetj67bn5h2/theyard-render:vN', ['The same version as the API,', 'the same registry, the same', 'sign-in.']],
      ['Point the web app at the tag', 'APP-THEYARD-RENDER-SS: DOCKER|...:vN', ['One call: every other setting', 'is infra/render.bicep\'s.', '']],
      ['Verify', 'its own /readyz', ['Ready as the version it was', 'built as, and says whether the', 'API has caught up.']],
    ],
  },
];

let y = 110;
const bw = 300;
const gap = 30;
for (const row of rows) {
  out.push(`  <rect x="30" y="${y}" width="${W - 60}" height="210" class="lane"/>`);
  out.push(`  <text x="50" y="${y + 30}" class="lane-title">${esc(row.name)}</text>`);
  out.push(`  <text x="50" y="${y + 50}" class="lane-sub">${esc(row.workflow)}, on every push to main</text>`);
  let x = 50;
  let prev = null;
  for (const [title, mono, lines] of row.steps) {
    const b = step(x, y + 66, bw, title, mono, lines);
    if (prev) out.push(`  <path d="M${prev.right} ${prev.midY} L${b.x} ${b.midY}" class="flow"/>`);
    prev = b;
    x += bw + gap;
  }
  y += 240;
}
const H = y + 60;

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>Every app TheYard runs ships the same way: build the Docker image, push it to the registry, point the web app at the tag, verify</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <style>
      .box { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .lane { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 600; }
      .lane-title { fill: #3f3a37; font-size: 17px; font-weight: 700; }
      .lane-sub { fill: #62666f; font-size: 13px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 10.5px; }
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
    </style>
  </defs>
  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="30" y="44" class="heading">One shape for every app that runs: a Docker image, by tag</text>
  <text x="30" y="68" class="caption">Both web apps run a container image built from this repository's own Dockerfile and pulled from the registry as the identity. Nothing is zip-deployed or built on the server.</text>
${out.join('\n')}
  <text x="30" y="${H - 26}" class="caption">Source: docs/images/deploy-shape.svg, drawn by docs/images/deploy-shape.mjs from the two workflows it names.</text>
</svg>
`;
writeFileSync(join(here, 'deploy-shape.svg'), svg, 'utf8');
console.log(`wrote ${join(here, 'deploy-shape.svg')} (${W} by ${H})`);
