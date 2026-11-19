// cosmos_emulator.mjs
//
// Does:      Waits for the Azure Cosmos DB emulator at the endpoint given (http://localhost:8081 on the gate's runner),
//            then makes the database and, under the prefix given, the containers named (every infra/cosmos/*.json when
//            none is), each with the partition key, time to live and indexing policy its file says. The gate names the
//            eight the account's tests- copy held, and no catalogue among them, so a suite booted on Cosmos DB finds
//            what it was written against. A container that is already there is left as it is.
// Does not:  Touch the Azure account. It refuses any endpoint that is not this machine, because the emulator's key
//            is public and the account has none (ADR-098, the addendum of @VER@).
// Used by:   .github/workflows/gate.yml, the cosmos job, before the Cosmos DB passes; and by hand on a laptop with the
//            emulator in docker.
import { createHmac } from 'node:crypto';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const [endpoint = 'http://localhost:8081', prefix = 'tests-', names = '', database = 'theyard'] =
  process.argv.slice(2);
const wanted = names ? new Set(names.split(',')) : null;
// The emulator's documented key, the same on every copy of it.
const key =
  'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==';

if (!['localhost', '127.0.0.1', '[::1]'].includes(new URL(endpoint).hostname)) {
  console.error(
    `cosmos_emulator.mjs: ${endpoint} is not this machine; it only sets up the emulator`
  );
  process.exit(1);
}

const call = async (method, path, type, link, body) => {
  const date = new Date().toUTCString();
  const text = `${method.toLowerCase()}\n${type}\n${link}\n${date.toLowerCase()}\n\n`;
  const signature = createHmac('sha256', Buffer.from(key, 'base64')).update(text).digest('base64');
  return fetch(`${endpoint.replace(/\/+$/, '')}/${path}`, {
    method,
    headers: {
      authorization: encodeURIComponent(`type=master&ver=1.0&sig=${signature}`),
      'x-ms-date': date,
      'x-ms-version': '2018-12-31',
      'content-type': 'application/json',
    },
    body: body && JSON.stringify(body),
  });
};

// The container starts in a few seconds; the gate gives it two minutes before calling the run red.
for (let tries = 0; ; tries++) {
  try {
    if ((await call('GET', 'dbs', 'dbs', '')).ok) break;
  } catch {}
  if (tries === 60) {
    console.error(`cosmos_emulator.mjs: no emulator at ${endpoint} after two minutes`);
    process.exit(1);
  }
  await new Promise((resolve) => setTimeout(resolve, 2000));
}

const made = async (response, what) => {
  if (response.ok || response.status === 409) return response.status === 409 ? 'there' : 'made';
  throw new Error(`${what}: ${response.status} ${await response.text()}`);
};

console.log(
  `database ${database}: ${await made(await call('POST', 'dbs', 'dbs', '', { id: database }), database)}`
);
const folder = join(import.meta.dirname, '..', 'infra', 'cosmos');
for (const file of readdirSync(folder)
  .filter((name) => name.endsWith('.json'))
  .sort()) {
  const shape = JSON.parse(readFileSync(join(folder, file), 'utf8'));
  if (!shape.container || (wanted && !wanted.has(shape.container))) continue;
  const id = `${prefix}${shape.container}`;
  const body = {
    id,
    partitionKey: { paths: [shape.partitionKeyPath], kind: 'Hash', version: 2 },
    indexingPolicy: shape.indexingPolicy,
    ...(shape.defaultTtlSeconds != null && { defaultTtl: shape.defaultTtlSeconds }),
  };
  const outcome = await made(
    await call('POST', `dbs/${database}/colls`, 'colls', `dbs/${database}`, body),
    id
  );
  console.log(`${id}: ${outcome}`);
}
