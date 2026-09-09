// Draws sql-vs-cosmos.svg (ADR: SQL Server and Cosmos DB, side by side) in the
// style of two-sites.svg: the same application on its two stores, Azure SQL
// Database on the left and Azure Cosmos DB on the right, one row per question a
// reader who knows one and not the other asks first. Every box names the file
// in this repository that holds what it describes, and every number is one the
// records measured. Run from the repo root:
//   node docs/images/sql-vs-cosmos.mjs          writes docs/images/sql-vs-cosmos.svg
//   node docs/images/sql-vs-cosmos.mjs --png    also renders sql-vs-cosmos.png at 2x in Chrome
// Redraw it when a store, a file or a number changes; the picture is a claim.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1520;

const STYLE = `
      .box { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .lane { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .lane-sql { fill: #eef1f6; stroke: #c9d1e0; stroke-width: 1; rx: 14; }
      .lane-cosmos { fill: #f5f0ec; stroke: #e0d4cb; stroke-width: 1; rx: 14; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 600; }
      .lane-title { fill: #3f3a37; font-size: 19px; font-weight: 700; letter-spacing: 0.02em; }
      .lane-sub { fill: #62666f; font-size: 13px; }
      .row { fill: #3f3a37; font-size: 14px; font-weight: 700; letter-spacing: 0.04em; text-transform: uppercase; }
      .row-sub { fill: #62666f; font-size: 12.5px; font-style: italic; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 10.5px; }
      .num { fill: #536786; font-size: 12.5px; font-weight: 600; }
      .num-cosmos { fill: #8a6a4f; font-size: 12.5px; font-weight: 600; }
      .rule { stroke: #d8d3d4; stroke-width: 1; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Greedy word wrap at a character budget; the budget leaves room for Poppins' width. */
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

const out = [];

/** One box: a title, a monospace file line, wrapped paragraphs, an optional number line. Returns its bottom. */
function box(x, y, w, title, mono, paragraphs, number, numberClass, width) {
  const lines = paragraphs.flatMap((p) => wrap(p, width));
  const h = 70 + 17 * (lines.length - 1) + (number ? 34 : 14);
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" class="box"/>`);
  out.push(`  <text x="${x + 15}" y="${y + 26}" class="title">${esc(title)}</text>`);
  out.push(`  <text x="${x + 15}" y="${y + 48}" class="mono">${esc(mono)}</text>`);
  lines.forEach((line, j) => {
    out.push(`  <text x="${x + 15}" y="${y + 70 + 17 * j}" class="body">${esc(line)}</text>`);
  });
  if (number) {
    out.push(`  <text x="${x + 15}" y="${y + 70 + 17 * (lines.length - 1) + 22}" class="${numberClass}">${esc(number)}</text>`);
  }
  return y + h;
}

// ===================== The two lanes =====================
const LEFT = { x: 40, w: 700 };
const RIGHT = { x: 780, w: 700 };
const bx = (lane) => lane.x + 20;
const bw = (lane) => lane.w - 40;
const WIDTH = 78;

/**
 * One row: a heading across both lanes, then a box per side. The rows are
 * the questions in the order the teaching record answers them (ADR: Cosmos
 * DB, explained for someone who knows SQL Server); each side's file is the
 * one a reader opens next.
 */
const rows = [
  {
    heading: 'What it is',
    sub: 'the model each engine enforces',
    sql: {
      title: 'A relational database',
      mono: 'Azure SQL Database, General Purpose serverless, the free limit (ADR-039)',
      body: [
        'Tables, rows, foreign keys and T-SQL. The schema is the contract and the engine enforces it: a row that breaks a constraint is refused before it lands.',
        'The server sits one region away from the container, which is the fact behind most of the numbers below.',
      ],
    },
    cosmos: {
      title: 'A document database',
      mono: 'Azure Cosmos DB for NoSQL, account cosmos-theyard-ss, free tier (ADR-059)',
      body: [
        'JSON documents in containers, each container split by a partition key. The document owns its shape; the engine enforces the key, the id and the etag, and nothing else.',
        'The account is in the container\'s own region, so a round trip is milliseconds, not tens of them.',
      ],
    },
  },
  {
    heading: 'How the application reaches it',
    sub: 'the adapter behind the same three ports',
    sql: {
      title: 'Entity Framework Core over SqlClient',
      mono: 'api/TheYard.Infrastructure/YardConnection.cs, EfSources.cs, SqlLogInterceptor.cs',
      body: [
        'A DbContext per request, LINQ that EF turns into statements, and a managed identity token instead of a password. The interceptor records every statement, parameters by name and never a value (ADR-043).',
      ],
    },
    cosmos: {
      title: 'The Cosmos DB SDK, directly',
      mono: 'api/TheYard.Infrastructure.Cosmos/CosmosStore.cs, CosmosSources.cs',
      body: [
        'One CosmosClient for the process, point reads and queries by hand, so the request charge and the diagnostics stay visible. Managed identity with a data-plane role; local authentication is disabled, so no key exists to leak (ADR-062).',
      ],
    },
  },
  {
    heading: 'A bid, at rest',
    sub: 'the same fact, stored two ways',
    sql: {
      title: 'One row in dbo.Bids',
      mono: 'api/TheYard.Database/Tables/Bids.sql',
      body: [
        'PRIMARY KEY (UserId, VehicleId). A FOREIGN KEY to AspNetUsers with ON DELETE CASCADE, so deleting an account takes its bids. A rowversion column the engine maintains, so a lost update is impossible to write by accident.',
      ],
    },
    cosmos: {
      title: 'One document in the bids container',
      mono: 'infra/cosmos/bids.json, partition key /user_id',
      body: [
        'id is the vehicle, the partition is the buyer, so every write is a point write inside one partition. The etag is the concurrency token. No foreign key exists; the code keeps that rule, and the indexing policy excludes every path because nothing queries them.',
      ],
    },
  },
  {
    heading: 'Writing a bid',
    sub: 'read, decide, write, and what happens when two writers race',
    sql: {
      title: 'Find, then Add or update, then SaveChanges',
      mono: 'api/TheYard.Infrastructure/EfSources.cs, region bid-store',
      body: [
        'Two statements. A DbUpdateConcurrencyException means another writer moved the row between the read and the write; the store starts again from what is there now, three tries, then the exception travels.',
      ],
      number: 'measured from the container: 84 ms, 2 statements (ADR-067)',
    },
    cosmos: {
      title: 'Point read, then CreateItem or ReplaceItem with If-Match',
      mono: 'api/TheYard.Infrastructure.Cosmos/CosmosSources.cs, region cosmos-bid-store',
      body: [
        'Two operations. A 412 is the same race as the concurrency exception and a 409 is a create that lost; the same three tries. The read costs 1 RU, the create 5.52, the replace 10.29.',
      ],
      number: 'measured from the container: 10 ms, 2 operations, 6.52 RU (ADR-067)',
    },
  },
  {
    heading: 'Keeping an address unique',
    sub: 'the guarantee one engine gives and the other has to be built',
    sql: {
      title: 'A unique index does it',
      mono: 'api/TheYard.Database/Tables/Identity/AspNetUsers.sql, UserNameIndex',
      body: [
        'The address is the user name here, and the unique index on NormalizedUserName refuses a second account with the same one. Nothing in the application has to remember to check.',
      ],
    },
    cosmos: {
      title: 'A claim document does it',
      mono: 'api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs, region create',
      body: [
        'No unique index spans partitions. The store creates a document whose id is the address first; a 409 on that create is the refusal. If the account write then fails the claim is removed, and a process that dies between the two leaves a claim with no account, which is the written-down cost of the shape (ADR-061).',
      ],
    },
  },
  {
    heading: 'Indexes, and what a query costs',
    sub: 'where the design work goes on each side',
    sql: {
      title: 'Indexes chosen per query, in the DACPAC',
      mono: 'api/TheYard.Database (the SQL project), ADR-040',
      body: [
        'A clustered primary key, nonclustered indexes where a query needs them, and a plan the engine picks. The cost of a query is compute time and it shows up as vCore-seconds against the free limit.',
      ],
    },
    cosmos: {
      title: 'An indexing policy per container, in JSON',
      mono: 'infra/cosmos/*.json, ADR-058',
      body: [
        'Every path indexed by default, and here every path excluded on the four site containers because they are read by id. The cost of an operation is request units, charged per call, and the wrong partition key turns a 1 RU read into a fan-out across every partition.',
      ],
      number: 'the experiment: 8.84 RU per seeded document tuned, 16.07 under the default (ADR-058)',
    },
  },
  {
    heading: 'Consistency and transactions',
    sub: 'how far one write can reach',
    sql: {
      title: 'ACID across tables',
      mono: 'one transaction, any number of tables, the engine arbitrates',
      body: [
        'A transaction can touch every table, row versioning keeps readers off writers, and the database is the single arbiter of what happened in what order.',
      ],
    },
    cosmos: {
      title: 'Session consistency, atomic inside one partition',
      mono: 'TransactionalBatch, one partition key at a time',
      body: [
        'A reset deletes one buyer\'s bids as one batch because they share a partition. Nothing spans partitions atomically; the application is arranged so nothing needs to.',
      ],
    },
  },
  {
    heading: 'What the Admin tab shows',
    sub: 'the same page on both sites',
    sql: {
      title: 'Every statement, with its time',
      mono: 'the SQL card and the console log, category Microsoft.EntityFrameworkCore.Database.Command',
      body: [
        'The statement, its parameters by name, the milliseconds. On the SQL site the Timing card\'s SQL line is the ring these feed (ADR-043).',
      ],
    },
    cosmos: {
      title: 'Every operation, with its charge',
      mono: 'the operations card and the console log, category TheYard.Infrastructure.Cosmos.CosmosStore',
      body: [
        'The operation, the container, the request units, the milliseconds and whether it named a partition or fanned out. Since 1.0.0.103 each is also one log line beside the SQL ones (ADR-062).',
      ],
    },
  },
  {
    heading: 'Measured on this application',
    sub: 'twenty paired rounds from Missouri, and the proof from the container itself',
    sql: {
      title: 'The relational side',
      mono: 'ADR-064 (the client), ADR-067 (the proof)',
      body: [
        'From Missouri: bid write 219 ms, sign in 249 ms, register 319 ms at p50. From the container: 39 ms of every operation is the round trip to a server one region away.',
      ],
      number: 'the proof: on 3 of 8 paths the same time; on the other 5 the difference is the round trip',
    },
    cosmos: {
      title: 'The document side',
      mono: 'ADR-064 (the client), ADR-067 (the proof)',
      body: [
        'From Missouri: bid write 135 ms, sign in 221 ms, register 216 ms at p50. From the container: 2 ms round trip, and every sign-in costs exactly 2 RU.',
      ],
      number: 'taking one round trip per operation off each side leaves them the same',
    },
  },
  {
    heading: 'What it costs this month',
    sub: 'both stores at $0.00, read from the meters',
    sql: {
      title: 'The free limit',
      mono: '100,000 vCore-seconds and 32 GB a month, renewing, ADR-039',
      body: [
        'Serverless compute that auto-pauses when nobody visits, so the first request after a quiet stretch waits for it to wake (a 25 s health check was measured once). The cost so far is $0.00.',
      ],
    },
    cosmos: {
      title: 'The free tier',
      mono: '1000 RU/s and 25 GB for the life of the account, ADR-059',
      body: [
        'Shared across the database\'s containers, never paused, and the site\'s whole month is pennies of it at serverless rates. The store costs $0.00; the second container group that serves it is about $34 a month at list.',
      ],
    },
  },
];

let y = 176;
const rowTops = [];
for (const row of rows) {
  rowTops.push(y);
  out.push(`  <text x="${LEFT.x + 20}" y="${y + 6}" class="row">${esc(row.heading)}</text>`);
  out.push(`  <text x="${LEFT.x + 20}" y="${y + 24}" class="row-sub">${esc(row.sub)}</text>`);
  const top = y + 38;
  const leftBottom = box(bx(LEFT), top, bw(LEFT), row.sql.title, row.sql.mono, row.sql.body, row.sql.number, 'num', WIDTH);
  const rightBottom = box(bx(RIGHT), top, bw(RIGHT), row.cosmos.title, row.cosmos.mono, row.cosmos.body, row.cosmos.number, 'num-cosmos', WIDTH);
  y = Math.max(leftBottom, rightBottom) + 28;
  out.push(`  <line x1="${LEFT.x + 20}" y1="${y - 14}" x2="${RIGHT.x + RIGHT.w - 20}" y2="${y - 14}" class="rule"/>`);
}
const lanesBottom = y - 6;
const captions = [
  'The records behind the left column: ADR-039 The SQL Server backend, ADR-040 Data first, and the database in source control, ADR-041 Two providers and a SQL project, explained, ADR-043 What the database is actually doing. Behind the right: ADR-058 The partition key, ADR-059 A second store on Cosmos DB, and what it costs, ADR-060 The ports learn to wait, ADR-061 Accounts on a document store, ADR-062 What the store is actually doing.',
  'Across both: ADR-063 Backends, side by side; ADR-064 Measuring both stores; ADR-065 Cosmos DB, explained for someone who knows SQL Server; ADR-066 One container, both stores; ADR-067 Same performance, proven; ADR-069 A permanent address for the second site.',
  'Both container groups open both stores; each site serves its default and the Store bar links to the other at the same page. A measurement can name the other store for one request with the X-Yard-Store header.',
  'Source: docs/images/sql-vs-cosmos.svg in the repository, drawn by docs/images/sql-vs-cosmos.mjs and redrawn when a store, a file or a number changes.',
].flatMap((caption) => wrap(caption, 190));
const H = lanesBottom + 40 + 20 * captions.length + 40;
const footer = captions
  .map((line, j) => `  <text x="40" y="${lanesBottom + 46 + 20 * j}" class="caption">${esc(line)}</text>`)
  .join('\n');

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="Poppins, 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>SQL Server and Cosmos DB, side by side: the same application on Azure SQL Database (left) and Azure Cosmos DB (right), row by row</title>
  <defs>
    <style>${STYLE}    </style>
  </defs>

  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="40" y="44" class="heading">SQL Server and Cosmos DB, side by side: one application, two stores, the same rows</text>
  <text x="40" y="68" class="caption">Read across. Each row is a question a developer who knows one of these and not the other asks first; each box names the file in this repository that answers it, and each number is one a record measured.</text>

  <!-- ===================== The two lanes ===================== -->
  <rect x="${LEFT.x}" y="92" width="${LEFT.w}" height="${lanesBottom - 92}" class="lane-sql"/>
  <text x="${LEFT.x + 20}" y="122" class="lane-title">Azure SQL Database</text>
  <text x="${LEFT.x + 20}" y="142" class="lane-sub">The live site's default store: https://theyard.stevenstout.biz</text>

  <rect x="${RIGHT.x}" y="92" width="${RIGHT.w}" height="${lanesBottom - 92}" class="lane-cosmos"/>
  <text x="${RIGHT.x + 20}" y="122" class="lane-title">Azure Cosmos DB for NoSQL</text>
  <text x="${RIGHT.x + 20}" y="142" class="lane-sub">The second site's default store: https://theyard-cosmos.stevenstout.biz</text>

${out.join('\n')}

${footer}
</svg>
`;

const svgPath = join(here, 'sql-vs-cosmos.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(
    `<!doctype html><html><head>
      <link href="https://fonts.googleapis.com/css2?family=Poppins:wght@400;500;600;700&display=swap" rel="stylesheet">
      <style>html,body{margin:0;background:#e9e6e7}</style>
    </head><body>${svg}</body></html>`,
    { waitUntil: 'networkidle' }
  );
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'sql-vs-cosmos.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}
