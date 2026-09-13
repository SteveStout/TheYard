import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { signIn } from './signIn';

test('the Admin tab shows the running system reporting on itself', async ({ page, request }) => {
  await openTheYard(page);
  await page
    .getByRole('navigation', { name: 'Project documents' })
    .getByRole('button', { name: 'Admin', exact: true })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  // The health card waits on every store the container runs. On the gate's
  // Cosmos DB pass that is two real stores reached from this machine, each
  // behind a token acquisition on a cold process, and the relational one is a
  // serverless database that wakes in tens of seconds after a quiet stretch
  // (the connection carries a sixty-second resume budget for exactly that,
  // YardConnection's resume-budget region). Five seconds was the default
  // assertion budget, and on 2026-09-09 the card was still loading at five on
  // a pass that was otherwise green; the wait is the application's own, so
  // the assertion gets most of the test's minute rather than a twelfth of it.
  await expect(page.getByTestId('health-card')).toContainText('healthy', { timeout: 45_000 });
  // Every check shows how long it took (ADR-010, second pass).
  await expect(page.getByTestId('check-duration').first()).toHaveText(/^\d+ ms$/);
  expect(await page.getByTestId('check-duration').count()).toBeGreaterThanOrEqual(3);
  await expect(page.getByTestId('errors-card')).toBeVisible();
  await expect(page.getByTestId('azure-card')).toBeVisible();
  // Telemetry is wired at deploy time, so a local run must render the card's
  // "not configured" state rather than an empty box or a crash (ADR-024).
  await expect(page.getByTestId('telemetry-card')).toBeVisible();
  await expect(page.getByTestId('telemetry-card')).toContainText('Traffic, last hour');
  await expect(page.getByTestId('timing-card')).toContainText('Path');
  // The status summary reads as a sentence. It used to render "367 of 200",
  // which is two numbers and no relationship between them, on the page whose
  // whole job is being readable by somebody who did not write it.
  await expect(page.getByTestId('timing-card')).toContainText(/Answers: \d+ with status \d{3}/);
  // The two stores get the same two lines, and a container with no document
  // store says so in words rather than leaving the line out (ADR: Backends,
  // side by side, the addendum on parity). The ship gate runs this on both
  // shapes: SQLite alone, and both stores with the document one the default.
  await expect(page.getByTestId('timing-sql')).toHaveText(
    /^SQL: p50 \d+ ms, p95 \d+ ms, slowest \d+ ms\.$/
  );
  const shape = (await (await request.get('http://localhost:5210/api/stores')).json()) as {
    stores: unknown[];
  };
  await expect(page.getByTestId('timing-store')).toHaveText(
    shape.stores.length > 1
      ? /^Document store: p50 \d+ ms, p95 \d+ ms, slowest \d+ ms, [\d.]+ RU over the window, /
      : /^Document store: none on this container/
  );
  // The SQL card on a relational container, the operations card on the
  // document one: the same page, whichever store it is on (ADR: What the store
  // is actually doing).
  // One card on a one-store container, both on a container running both; either way the first is visible.
  await expect(
    page.getByTestId('sql-card').or(page.getByTestId('store-card')).first()
  ).toBeVisible();
  await expect(page.getByTestId('log-card')).toContainText('Category');
  // The document store gets a console line per operation, the way every SQL
  // statement gets one (ADR: What the store is actually doing, addendum); on
  // the two-store shape the log card shows it.
  if (shape.stores.length > 1) {
    await expect(page.getByTestId('log-card')).toContainText('Executed Cosmos DB');
  }
  await page.getByRole('button', { name: 'Back to inventory' }).click();
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
});

test('a browser error reaches the Admin tab (ADR-023)', async ({ page, request }) => {
  const marker = `e2e boundary probe ${Date.now()}`;
  const posted = await request.post('http://localhost:5210/api/errors/client', {
    data: { message: marker, stack: 'at VehicleCard', path: '/?probe=1' },
  });
  expect(posted.status()).toBe(204);

  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('errors-card')).toContainText(marker);
});

test('?view=admin deep-links straight to the Admin tab', async ({ page }) => {
  await openTheYard(page, '/?view=admin');
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
});

test('the SQL section shows statements and never a parameter value', async ({ page, request }) => {
  // Register through the API so the browser is not the thing under test here.
  // A registration is the request whose parameters carry an email address, and
  // it is the reason this section shows names and types and nothing else.
  const email = `sql-canary-${Date.now()}@example.com`;
  const registered = await request.post('http://localhost:5210/api/auth/register', {
    data: { email, password: 'correct horse battery' },
  });
  expect(registered.status(), await registered.text()).toBe(200);

  // Which store this API is on decides which card the page shows, and the
  // test holds the same rule against both (ADR: What the store is actually doing).
  const store = (await (await request.get('http://localhost:5210/api/admin/store')).json()) as {
    store: string;
  };
  const cosmos = store.store === 'Azure Cosmos DB';

  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId(cosmos ? 'store-card' : 'sql-card');
  await expect(card).toBeVisible();
  // A statement, with the request that caused it and a parameter described;
  // on the document store, an operation on the users container, pinned to a
  // partition that is described and never named.
  await expect(card).toContainText(cosmos ? 'users' : 'AspNetUsers');
  await expect(card).toContainText('POST /api/auth/register');
  await expect(card).toContainText(cosmos ? /pinned to the (account|address)/ : /@\w+ \w+/);
  // The address itself is nowhere on the page.
  await expect(page.locator('body')).not.toContainText(email);
  await expect(page.locator('body')).not.toContainText('sql-canary');
});

// #region backends-card
test('the comparison card stands when there is no peer to compare with (ADR: Backends, side by side)', async ({
  page,
  request,
}) => {
  // A local run has no peer configured, which is the first of the four ways the
  // peer column can be empty, and the card has to read as a card rather than
  // as an error: this container's column filled in, the other's saying why not,
  // and every other card on the page untouched. A run with both stores in the
  // one process compares those two instead, and says so (ADR: One container,
  // both stores).
  const stores = (await (await request.get('http://localhost:5210/api/stores')).json()) as {
    stores: { name: string }[];
  };
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('backends-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('Backends, side by side');
  if (stores.stores.length > 1) {
    await expect(card).toContainText('Both stores run in this container');
    for (const store of stores.stores) {
      await expect(card.getByRole('columnheader', { name: new RegExp(store.name) })).toBeVisible();
    }
  } else {
    await expect(card).toContainText('No peer is configured on this container');
    await expect(card).toContainText(/This site \((SQLite|Azure SQL Database|Azure Cosmos DB)\)/);
  }
  // The label and the number are neighbouring cells, and a cell boundary is
  // no whitespace at all in the text Playwright reads, so \s* rather than \s+.
  await expect(card).toContainText(/Cold start, process start to ready\s*\d+ ms/);
  await expect(card).toContainText(/Catalogue load\s*\d+ ms/);
  await expect(card).toContainText('Bid write');
  await expect(card).toContainText('Sign in');
  await expect(page.getByTestId('health-card')).toContainText('healthy');
  // One card on a one-store container, both on a container running both; either way the first is visible.
  await expect(
    page.getByTestId('sql-card').or(page.getByTestId('store-card')).first()
  ).toBeVisible();
});
// #endregion backends-card

// #region experiment-card
test('the partition key card explains itself when there is no catalogue to query (ADR: The partition key)', async ({
  page,
  request,
}) => {
  const store = (await (await request.get('http://localhost:5210/api/admin/store')).json()) as {
    store: string;
  };
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('experiment-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('The partition key, live');
  if (store.store === 'Azure Cosmos DB') {
    // Against the test containers the catalogue is either seeded or not; both
    // are sentences on the card rather than an empty box.
    await expect(card).toContainText(/(Not available here|documents on \d+ physical partition)/);
  } else {
    await expect(card.getByTestId('experiment-note')).toContainText('not on Azure Cosmos DB');
  }
});
// #endregion experiment-card

// #region proof-card
test('the proof card offers a run and says what it needs (ADR: Same performance, proven)', async ({
  page,
  request,
}) => {
  // A two-store run takes the proof about a minute; the default budget is one.
  test.setTimeout(180_000);
  const stores = (await (await request.get('http://localhost:5210/api/stores')).json()) as {
    stores: { name: string }[];
  };
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('proof-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('Same performance, proven');
  // Signed out, the button says what it needs and does nothing: starting a
  // run is a write (ADR: The one write a stranger can make, addendum).
  const run = card.getByTestId('proof-run');
  await expect(run).toBeDisabled();
  await expect(run).toHaveText('Sign in to run the proof');
  await signIn(page);
  await openTheYard(page, '/?view=admin');
  await expect(run).toBeEnabled();
  await run.click();
  // On one store the run fails at once with its reason; on two it runs for a
  // while and lands a sentence. Either is a sentence on the card, never a hang.
  if (stores.stores.length < 2) {
    await expect(card.getByTestId('proof-note')).toContainText('one store', { timeout: 30_000 });
  } else {
    // The start is asserted on its own, so a POST that did not land fails
    // here in seconds with the button's own words, not two minutes later as
    // a sentence that never came (the 1.0.0.109 gate, take one).
    await expect(run).toHaveText('Running…', { timeout: 15_000 });
    await expect(card.getByTestId('proof-sentence')).toBeVisible({ timeout: 120_000 });
    await expect(card).toContainText('Bid write');
  }
});
// #endregion proof-card

// #region activity-card
/**
 * Site activity (ADR: Site activity, and the line an address does not cross).
 * The graph draws, one line per store the container runs; the totals read as
 * a sentence; and the rule the feature is built on is asserted on the wire:
 * neither response carries an at sign, so no email address can be in it,
 * whatever a visitor put in a URL.
 */
test('the activity graph draws at the top of the tab and its response names nobody', async ({
  page,
  request,
}) => {
  // A few requests with an address in the path and a query string, so there
  // is something in the window and the thing that must not appear was offered.
  await request.get('http://localhost:5210/api/vehicles?limit=1&who=someone@example.com');
  await request.get('http://localhost:5210/api/vehicles/someone@example.com');
  const stores = (await (await request.get('http://localhost:5210/api/stores')).json()) as {
    stores: { key: string }[];
  };
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('activity-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('Site activity');
  await expect(card.getByTestId('activity-graph')).toBeVisible();
  for (const store of stores.stores) {
    await expect(card.getByTestId(`activity-line-${store.key}`)).toHaveCount(1);
  }
  await expect(card.getByTestId('activity-totals')).toContainText(/\d+ requests in the window/);
  // A week is the default; a month is a change, and the graph redraws for it.
  await expect(card.getByTestId('activity-window-7d')).toHaveAttribute('aria-pressed', 'true');
  await card.getByTestId('activity-window-30d').click();
  await expect(card.getByTestId('activity-window-30d')).toHaveAttribute('aria-pressed', 'true');
  await expect(card.getByTestId('activity-graph')).toHaveAttribute('aria-label', /30d window/);
  // And a click on the window already showing changes nothing and blanks nothing.
  await card.getByTestId('activity-window-30d').click();
  await expect(card.getByTestId('activity-graph')).toBeVisible();

  // The wire, for both windows the page just asked for.
  for (const window of ['24h', '7d', '30d']) {
    const response = await request.get(`http://localhost:5210/api/admin/activity?window=${window}`);
    expect(response.ok()).toBe(true);
    const body = await response.text();
    expect(body).not.toContain('@');
    expect(body).toContain('"series"');
  }
  const refused = await request.get('http://localhost:5210/api/admin/activity?window=1y');
  expect(refused.status()).toBe(400);
});

test('the visitor table exists only behind the key, and its response names nobody', async ({
  page,
  request,
}) => {
  await request.get('http://localhost:5210/api/vehicles?limit=1');
  // Without the key, or with a wrong one, the endpoint does not exist.
  expect((await request.get('http://localhost:5210/api/admin/activity/visitors')).status()).toBe(
    404
  );
  expect(
    (
      await request.get('http://localhost:5210/api/admin/activity/visitors', {
        headers: { 'X-Admin-Key': 'not-the-key' },
      })
    ).status()
  ).toBe(404);
  // With it, the rows: a token, a network to three octets and an x, a store,
  // and never an at sign. The key is the one playwright.config.ts hands the
  // API for this run and nothing else. The collector writes every five
  // seconds, so the first row is waited for rather than assumed.
  const visitorsUrl = 'http://localhost:5210/api/admin/activity/visitors?window=24h';
  const headers = { 'X-Admin-Key': 'e2e-admin-key' };
  await expect
    .poll(
      async () => {
        const r = await request.get(visitorsUrl, { headers });
        return r.ok() ? ((await r.json()) as { count: number }).count : -1;
      },
      { timeout: 20_000 }
    )
    .toBeGreaterThan(0);
  const admitted = await request.get(visitorsUrl, { headers });
  expect(admitted.ok()).toBe(true);
  const body = await admitted.text();
  expect(body).not.toContain('@');
  const rows = JSON.parse(body) as {
    visitors: { visitor: string; network: string; store: string }[];
  };
  expect(rows.visitors.length).toBeGreaterThan(0);
  for (const row of rows.visitors) {
    expect(row.visitor).toMatch(/^[0-9a-f]{32}$/);
    expect(row.network).toMatch(/x$/);
    expect(row.network).not.toMatch(/^\d+\.\d+\.\d+\.\d+$/);
  }

  // On the page: no table without the key in the address bar, a table with it.
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('activity-visitors')).toHaveCount(0);
  await openTheYard(page, '/?view=admin&key=e2e-admin-key');
  const table = page.getByTestId('activity-visitors');
  await expect(table).toBeVisible();
  await expect(table.getByRole('columnheader', { name: 'Requests' })).toBeVisible();
  // Grouped by day: at least today's heading row sits above its visitors.
  expect(await table.getByTestId('activity-day').count()).toBeGreaterThanOrEqual(1);
  await table.getByRole('button', { name: 'Requests' }).click();
  await expect(table.locator('tbody tr').first()).toBeVisible();
});
// #endregion activity-card

// #region kept-logs-card
test('the kept log is a 404 without the key, carries no at sign with it, and the page shows it grouped by day', async ({
  page,
  request,
}) => {
  // A request with an address in its path, so the rule has something to hold.
  await request.get('http://localhost:5210/api/vehicles/someone@example.com');
  expect((await request.get('http://localhost:5210/api/admin/logs/kept')).status()).toBe(404);
  expect(
    (
      await request.get('http://localhost:5210/api/admin/logs/kept', {
        headers: { 'X-Admin-Key': 'not-the-key' },
      })
    ).status()
  ).toBe(404);

  const headers = { 'X-Admin-Key': 'e2e-admin-key' };
  const url = 'http://localhost:5210/api/admin/logs/kept?window=24h&kind=request&path=someone';
  const first = await request.get(url, { headers });
  expect(first.ok()).toBe(true);
  const kept = (
    (await first.json()) as { kept: { available: boolean }; counts: { kind: string }[] }
  ).kept.available;
  // The collector writes once a minute on the live site and every two
  // seconds here (playwright.config.ts hands the API Logs__DrainSeconds), so
  // the first line is waited for rather than assumed. The SQLite run keeps
  // nothing and asserts the honest empty answer instead.
  if (kept) {
    await expect
      .poll(
        async () => {
          const r = await request.get(url, { headers });
          return r.ok() ? ((await r.json()) as { count: number }).count : -1;
        },
        { timeout: 20_000 }
      )
      .toBeGreaterThan(0);
  }
  const admitted = await request.get(url, { headers });
  const body = await admitted.text();
  expect(body).not.toContain('@');
  const parsed = JSON.parse(body) as {
    counts: { kind: string; count: number }[];
    events: { kind: string; path: string; network: string }[];
  };
  expect(parsed.counts.map((c) => c.kind)).toEqual(['request', 'error', 'app']);
  if (kept) {
    expect(parsed.events.some((e) => e.path.includes('someone%40example.com'))).toBe(true);
    for (const e of parsed.events) expect(e.network).toMatch(/x$/);
  } else {
    expect(parsed.events).toEqual([]);
  }

  // On the page: a keyless note without the key, the card with it, narrowed
  // to requests whose path contains the marker.
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('kept-logs-keyless')).toBeVisible();
  await expect(page.getByTestId('kept-logs')).toHaveCount(0);
  await openTheYard(page, '/?view=admin&key=e2e-admin-key');
  const card = page.getByTestId('kept-logs-card');
  await expect(card.getByTestId('kept-logs-summary')).toBeVisible();
  await card.getByTestId('kept-logs-kind').selectOption('request');
  await card.getByTestId('kept-logs-path').fill('someone');
  await card.getByTestId('kept-logs-apply').click();
  const table = card.getByTestId('kept-logs');
  await expect(table).toBeVisible();
  if (kept) {
    expect(await table.getByTestId('kept-logs-day').count()).toBeGreaterThanOrEqual(1);
    await expect(table.locator('tbody')).toContainText('someone%40example.com');
  }
  await expect(card).not.toContainText('@');
});
// #endregion kept-logs-card

// #region remembered-key
test('the browser remembers the key after one keyed visit, and forgets it on request', async ({
  page,
}) => {
  // Once with the key in the address bar, then plain: the operator's cards
  // still show, because the browser kept the key (the operator reads the
  // site from a phone, and the file the key lives in is on one machine).
  await openTheYard(page, '/?view=admin&key=e2e-admin-key');
  await expect(page.getByTestId('activity-visitors')).toBeVisible();
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('activity-visitors')).toBeVisible();
  await expect(page.getByTestId('kept-logs-keyless')).toHaveCount(0);

  // Forgetting takes effect at once and survives a reload.
  await page.getByTestId('admin-forget-key').click();
  await expect(page.getByTestId('activity-visitors')).toHaveCount(0);
  await expect(page.getByTestId('kept-logs-keyless')).toBeVisible();
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('activity-visitors')).toHaveCount(0);
  await expect(page.getByTestId('kept-logs-keyless')).toBeVisible();
});
// #endregion remembered-key
