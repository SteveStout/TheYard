// Draws compiled-ahead.svg (ADR: Compiled before it ships) in the style of
// server-page.svg: the same container twice, from the build to the first
// visitors after a deploy. On the left it ships as intermediate code and the
// runtime compiles every method on the plan's one shared core the first time
// it is called; on the right the publish step has already compiled the app
// and its packages to machine code, and the runtime compiles again only the
// methods that turn out hot. Every box is a step; the path under each title
// is where that step lives. Run from the repo root:
//   node docs/images/compiled-ahead.mjs          writes docs/images/compiled-ahead.svg
//   node docs/images/compiled-ahead.mjs --png    also renders compiled-ahead.png at 2x in Chrome
// Redraw it when a step moves; the picture is a claim about the code.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;

const STYLE = `
      .box { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .lane { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 600; }
      .lane-title { fill: #3f3a37; font-size: 17px; font-weight: 700; letter-spacing: 0.02em; }
      .lane-sub { fill: #62666f; font-size: 13px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .flow-label { fill: #4a6c96; font-size: 12px; font-weight: 500; }
      .loop { stroke: #ab978c; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow-taupe); }
      .loop-label { fill: #8a766b; font-size: 12px; font-weight: 500; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

/** Before 1.0.3.94, top to bottom: the runtime compiles every method on the one shared core. */
const RUNTIME = [
  ['The build', 'dotnet publish -c Release', [
    'The C# compiler writes intermediate language, IL: portable, and not yet machine code. The image carries the IL for this app and its packages, 221.1 MB in the registry with native files for every platform the packages know.']],
  ['The container starts', 'App Service, one instance, one core shared by both sites', [
    'The runtime loads the assemblies and finds IL. Nothing in them can run until it is compiled, and the only compiler on the machine is the one inside the running process.']],
  ['Every method, compiled at its first call', 'the JIT, on the plan\'s one core', [
    'Each method is turned into machine code the first time it is called, on the same core that is checking the schema, loading a hundred thousand vehicles and answering the first visitors. 32,690 methods in the first two minutes on 1.0.3.93, and 113 seconds of compiling counted across every thread.']],
  ['The hot methods, compiled again', 'tiered compilation with PGO', [
    'The runtime counts how often each method runs and compiles the hot ones a second and a third time at full optimization. 62,432 methods by the thirteenth minute, 66,685 by the seventy-fifth.']],
  ['The first visitors', '/readyz, then the listing', [
    'Ready 79 seconds after the process started on 1.0.3.93, most of it waits on the network that compiling cannot shorten; the compiling shared the core with those waits and with the first answers.']],
];
const RUNTIME_LABELS = {
  0: 'IL only',
  1: 'load, then compile',
  2: 'run, count, recompile',
  3: 'answer',
};

/** From 1.0.3.94, top to bottom: the publish step compiles ahead, the runtime compiles again only what is hot. */
const AHEAD = [
  ['The build', 'Dockerfile, region api-publish', [
    'Restore for linux-x64 with ReadyToRun asked for, which fetches the ahead-of-time compiler. Publish compiles this app and its packages to machine code for that platform and keeps the IL beside it. One switch; nothing in the code changes. 206.2 MB in the registry, 15 MB smaller, because one platform needs one set of native files.']],
  ['The container starts', 'App Service, the same plan', [
    'The runtime loads the assemblies and finds machine code already there for this app, EF Core, the Cosmos DB SDK and Identity. The framework in the aspnet base image shipped this way already.']],
  ['Runs as compiled', 'ReadyToRun code, the first tier', [
    'A method that arrived compiled runs without a visit to the JIT. The runtime compiled 12,273 to 16,525 methods itself at the first read across four rolls, 49 to 62 per cent fewer than before.']],
  ['The hot methods, compiled again', 'TieredCompilation and TieredPGO, api/TheYard.Api/TheYard.Api.csproj', [
    'Still on. Compiled-ahead code is the first tier; the methods that turn out hot are compiled again with instruments and then at full optimization, so a warmed process gives up nothing. That second compile is why the time spent compiling did not fall with the count.']],
  ['The first visitors', '/api/admin/metrics, region runtime-metrics', [
    'The reading that says what happened: methods the runtime compiled itself and its time compiling, beside the collector\'s mode. Ready read 71 to 161 seconds across the four rolls, on a core the plan\'s meter showed at 85 to 99 per cent from before the containers began.']],
];
const AHEAD_LABELS = {
  0: 'IL and machine code',
  1: 'load, then run',
  2: 'count, recompile the hot ones',
  3: 'answer, and report',
};

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Greedy word wrap at a character budget; the budget was set for Poppins' width, which IBM Plex Sans sits inside. */
function wrap(paragraph, width) {
  const lines = [];
  let line = '';
  for (const word of paragraph.split(' ')) {
    if (line && line.length + 1 + word.length > width) {
      lines.push(line);
      line = word;
    } else {
      line = line ? `${line} ${word}` : word;
    }
  }
  if (line) lines.push(line);
  return lines;
}

/** Stacks a lane's boxes from y0; returns each box's (y, h) and the bottom. */
function lane(x, w, boxes, labels, width, y0, out) {
  const bx = x + 20;
  const bw = w - 40;
  let y = y0;
  const geoms = [];
  boxes.forEach(([title, mono, paragraphs], i) => {
    const lines = paragraphs.flatMap((p) => wrap(p, width));
    const h = 70 + 17 * (lines.length - 1) + 14;
    out.push(`  <rect x="${bx}" y="${y}" width="${bw}" height="${h}" class="box"/>`);
    out.push(`  <text x="${bx + 15}" y="${y + 26}" class="title">${esc(title)}</text>`);
    out.push(`  <text x="${bx + 15}" y="${y + 48}" class="mono">${esc(mono)}</text>`);
    lines.forEach((line, j) => {
      out.push(`  <text x="${bx + 15}" y="${y + 70 + 17 * j}" class="body">${esc(line)}</text>`);
    });
    geoms.push([y, h]);
    if (i < boxes.length - 1) {
      const cx = bx + 60;
      out.push(`  <path d="M${cx} ${y + h} L${cx} ${y + h + 34}" class="flow"/>`);
      if (labels[i]) out.push(`  <text x="${cx + 12}" y="${y + h + 22}" class="flow-label">${esc(labels[i])}</text>`);
      y = y + h + 36;
    } else {
      y = y + h;
    }
  });
  return [geoms, y];
}

const body = [];
const [runtimeGeoms, runtimeBottom] = lane(40, 650, RUNTIME, RUNTIME_LABELS, 70, 170, body);
const [aheadGeoms, aheadBottom] = lane(710, 650, AHEAD, AHEAD_LABELS, 70, 170, body);
const H = Math.max(runtimeBottom, aheadBottom) + 112;

// The one step that moved: the compiling in the third box on the left is done at build time on the right.
const [jitY, jitH] = runtimeGeoms[2];
const [buildY, buildH] = aheadGeoms[0];
body.push(`  <path d="M${60 + 610} ${jitY + Math.floor(jitH / 2)} L695 ${jitY + Math.floor(jitH / 2)} L695 ${buildY + Math.floor(buildH / 2)} L730 ${buildY + Math.floor(buildH / 2)}" class="loop"/>`);
body.push(`  <text x="60" y="158" class="loop-label">The dashed line is the work that moved: from the container's first minutes to the build.</text>`);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>Compiled before it ships: the same container compiled by the runtime on its one shared core, and compiled ahead of time at the build</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <marker id="arrow-taupe" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#ab978c"/>
    </marker>
    <style>${STYLE}    </style>
  </defs>

  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="40" y="44" class="heading">Compiled before it ships: the same container, compiled by the runtime and compiled ahead of time</text>
  <text x="40" y="68" class="caption">Every box is a step; the path under each title is where that step lives. The plan has one core, shared by both sites, and every new container starts on it.</text>

  <!-- ===================== Lane 1: compiled by the runtime ===================== -->
  <rect x="40" y="92" width="650" height="${runtimeBottom + 20 - 92}" class="lane"/>
  <text x="60" y="120" class="lane-title">Compiled by the runtime: before 1.0.3.94</text>
  <text x="60" y="140" class="lane-sub">The image carries IL. Every method is compiled on the one core the first time it is called.</text>

  <!-- ===================== Lane 2: compiled ahead of time ===================== -->
  <rect x="710" y="92" width="650" height="${aheadBottom + 20 - 92}" class="lane"/>
  <text x="730" y="120" class="lane-title">Compiled ahead of time: 1.0.3.94 on</text>
  <text x="730" y="140" class="lane-sub">The image carries machine code too. The runtime compiles again only what turns out hot.</text>

${body.join('\n')}

  <text x="40" y="${H - 66}" class="caption">What held across four rolls: 49 to 62 per cent fewer methods compiled by the runtime at the first read, every time. What did not: no faster start measured, on a core the meter showed saturated through every roll.</text>
  <text x="40" y="${H - 46}" class="caption">What would settle it: the same load in Docker on one machine with the plan's limits and nothing else on the core, compiled ahead of time and not.</text>
  <text x="40" y="${H - 26}" class="caption">Source: docs/images/compiled-ahead.svg in the repository, drawn by docs/images/compiled-ahead.mjs and redrawn when a step moves.</text>
</svg>
`;

const svgPath = join(here, 'compiled-ahead.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(
    `<!doctype html><html><head>
      <link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&display=swap" rel="stylesheet">
      <style>html,body{margin:0;background:#e9e6e7}</style>
    </head><body>${svg}</body></html>`,
    { waitUntil: 'networkidle' }
  );
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'compiled-ahead.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}
