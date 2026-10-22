// Writes src/generated/stack.json from the repository (ADR: Technology versions, the addendum on the
// home page's table). The table itself is derived by src/lib/stackDerive.ts, the same function the test
// runs, so the two cannot disagree; this file only reads the disk and writes the result.
//
//   node scripts/stack.mjs          writes src/generated/stack.json
//   node scripts/stack.mjs --check  exits 1 when it is stale
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { deriveStack, stackText } from '../src/lib/stackDerive.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const files = {};
const take = (rel) => {
  files[rel] = fs.readFileSync(path.join(root, rel), 'utf8');
};
const walk = (dir, pattern) => {
  for (const entry of fs.readdirSync(path.join(root, dir), { withFileTypes: true })) {
    const rel = `${dir}/${entry.name}`;
    if (entry.isDirectory()) {
      if (!['bin', 'obj', 'node_modules'].includes(entry.name)) walk(rel, pattern);
    } else if (pattern.test(entry.name)) {
      take(rel);
    }
  }
};
walk('api', /\.csproj$/);
walk('functions', /\.csproj$/);
for (const name of fs.readdirSync(path.join(root, 'infra')))
  if (name.endsWith('.bicep')) take(`infra/${name}`);
for (const name of fs.readdirSync(path.join(root, '.github', 'workflows')))
  if (name.endsWith('.yml')) take(`.github/workflows/${name}`);
for (const one of [
  'package.json',
  'package-lock.json',
  'Dockerfile',
  'render/Dockerfile',
  'netlify.toml',
]) {
  if (fs.existsSync(path.join(root, one))) take(one);
}
const out = path.join(root, 'src', 'generated', 'stack.json');
const text = stackText(files);
const now = fs.existsSync(out) ? fs.readFileSync(out, 'utf8').replace(/\r\n/g, '\n') : '';
if (process.argv.includes('--check')) {
  process.stdout.write(
    now === text ? 'stack.json is current\n' : 'stack.json is stale: run node scripts/stack.mjs\n'
  );
  process.exit(now === text ? 0 : 1);
}
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, text);
const rows = deriveStack(files).layers.reduce((n, layer) => n + layer.rows.length, 0);
process.stdout.write(`wrote src/generated/stack.json: ${rows} rows\n`);
