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

test('every page this container serves is checked, and the card says what is down (ADR: Every page, checked at every roll)', async ({
  page,
  request,
}) => {
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('pages-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('Every page, checked');

  // The sweep runs when the container starts, so by the time a browser has
  // opened the tab it has either finished or is finishing. Eighty-odd
  // addresses on a cold process, each document expanding its live blocks as
  // it is served, is a second or two on a quiet machine and longer on a gate
  // running two suites beside itself; the button below is the fallback when
  // the cooldown has passed rather than the way in.
  const summary = card.getByTestId('pages-summary');
  await expect(summary).toBeVisible({ timeout: 120_000 });
  await expect(summary).toContainText(/\d+ of \d+ addresses answered/);
  await expect(card.getByTestId('pages-all-up')).toBeVisible();

  // Every address, and the same numbers on the wire as on the card.
  await card.getByTestId('pages-show-all').click();
  await expect(card.getByTestId('pages-table')).toContainText('/api/docs/performance');

  const wire = (await (await request.get('http://localhost:5210/api/admin/pages')).json()) as {
    status: string;
    report: {
      checked: number;
      up: number;
      entries: { address: string; kind: string; content_type: string | null }[];
    } | null;
  };
  expect(wire.report).not.toBeNull();
  expect(wire.report!.up).toBe(wire.report!.checked);
  expect(wire.report!.checked).toBeGreaterThan(50);
  const performance = wire.report!.entries.find(
    (entry) => entry.address === '/api/docs/performance'
  );
  expect(performance?.content_type).toBe('text/markdown');
});

test('the machines card shows the container, the relational store and the document store (ADR: What the machines are doing)', async ({
  page,
  request,
}) => {
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('machines-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('What the machines are doing');

  // The sampler takes its first reading as the process starts, so there is
  // always one by the time a browser has opened the tab.
  await expect(card.getByTestId('machines-container-line')).toContainText(/\d+(\.\d+)? MB of \d+/, {
    timeout: 60_000,
  });
  await expect(card.getByTestId('machines-container-table')).toContainText('MB');

  // The suite runs on SQLite, which keeps no resource view of itself, and the
  // card says which store it is looking at instead of drawing a zero. On the
  // Cosmos DB pass the document block carries the charges instead.
  const wire = (await (await request.get('http://localhost:5210/api/admin/machines')).json()) as {
    container: {
      memory_limit_mb: number;
      processors: number;
      samples: { working_set_mb: number }[];
    };
    relational: { available: boolean; note: string | null };
    document: { available: boolean; free_request_units_per_second: number };
  };
  expect(wire.container.samples.length).toBeGreaterThan(0);
  expect(wire.container.memory_limit_mb).toBeGreaterThan(0);
  expect(wire.container.processors).toBeGreaterThan(0);
  expect(wire.document.free_request_units_per_second).toBe(1000);

  // One chart per resource, drawn from the same readings the tables carry
  // (ADR: What the machines are doing, the addendum on drawing them). The
  // container always has one; the two stores have one each where the store
  // had something to say.
  await expect(card.getByTestId('machine-chart-container')).toBeVisible();
  await expect(card.getByTestId('machine-chart-container-memory')).toHaveCount(1);
  await expect(
    card.getByTestId('machine-chart-relational').or(card.getByTestId('machines-relational-note'))
  ).toBeVisible();
  await expect(
    card.getByTestId('machine-chart-document').or(card.getByTestId('machines-document-note'))
  ).toBeVisible();
  if (!wire.relational.available) {
    expect(wire.relational.note).toBeTruthy();
    await expect(card.getByTestId('machines-relational-note')).toBeVisible();
  } else {
    await expect(card.getByTestId('machines-relational-table')).toBeVisible();
  }
});

test('the machines card offers a day, a week and a month beside the hour, and a window with nothing kept says so (ADR: What the machines are doing)', async ({
  page,
  request,
}) => {
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('machines-card');
  await expect(card.getByTestId('machines-container-line')).toBeVisible({ timeout: 60_000 });

  // The hour is what the card opens on, and it is this process's own memory.
  // The window is chosen on the traffic card, and the machines card follows it.
  await expect(page.getByTestId('machines-window-1h')).toHaveAttribute('aria-pressed', 'true');
  await expect(card.getByTestId('machines-history')).toHaveCount(0);

  const wire = (await (
    await request.get('http://localhost:5210/api/admin/machines?window=24h')
  ).json()) as {
    windows: string[];
    history: { window: string; kept: boolean; available: boolean; bucket_minutes: number };
  };
  expect(wire.windows).toEqual(['1h', '24h', '7d', '30d']);
  expect(wire.history.window).toBe('24h');
  expect(wire.history.kept).toBe(true);
  expect(wire.history.bucket_minutes).toBe(5);

  await page.getByTestId('machines-window-24h').click();
  await expect(page.getByTestId('machines-window-24h')).toHaveAttribute('aria-pressed', 'true');
  if (wire.history.available) {
    // The Cosmos DB pass: the window is kept, and the card says how much of it
    // the store holds rather than drawing a full line through a day it was not
    // running for.
    await expect(card.getByTestId('machines-history-line')).toContainText('of 288 buckets', {
      timeout: 30_000,
    });
  } else {
    // SQLite has no document store beside it, so nothing is kept, and the card
    // says that in words instead of drawing an empty chart.
    await expect(card.getByTestId('machines-history-note')).toContainText('is not kept here', {
      timeout: 30_000,
    });
  }
  // The hour is still on the card under it, labelled as what it is.
  await expect(card).toContainText('as this process remembers the last hour');
});

test('the traffic card draws how busy, how fast and how many errors, a minute at a time (ADR: The Admin tab, as a product)', async ({
  page,
  request,
}) => {
  // Something to draw: a few real requests, which the ring keeps and this tab's own reads do not add to.
  for (const path of ['/api/vehicles?limit=5', '/api/facets', '/api/vehicles/not-a-vehicle']) {
    await request.get(`http://localhost:5210${path}`);
  }
  const wire = (await (await request.get('http://localhost:5210/api/admin/machines')).json()) as {
    traffic: {
      ring: number;
      minutes: {
        at: string;
        requests: number;
        p50_ms: number;
        p95_ms: number;
        client_errors: number;
      }[];
    };
  };
  expect(wire.traffic.ring).toBe(500);
  expect(wire.traffic.minutes.length).toBeGreaterThan(0);
  expect(wire.traffic.minutes.reduce((sum, minute) => sum + minute.requests, 0)).toBeGreaterThan(2);
  // The vehicle that does not exist is a 404, and a 404 is counted as one.
  expect(
    wire.traffic.minutes.reduce((sum, minute) => sum + minute.client_errors, 0)
  ).toBeGreaterThan(0);

  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('traffic-card');
  // Four numbers in plain words, in place of one sentence in status codes and percentiles.
  const requests = card.getByTestId('traffic-stat-requests');
  await expect(requests).toContainText('requests in the last hour', { timeout: 60_000 });
  await expect(requests).toContainText(/\d/);
  await expect(card.getByTestId('traffic-stat-typical')).toContainText('Typical answer');
  await expect(card.getByTestId('traffic-stat-slow')).toContainText('Slow answers');
  await expect(card.getByTestId('traffic-stat-errors')).toContainText('turned away');
  await expect(card.getByTestId('traffic-chart-requests')).toBeVisible();
  await expect(card.getByTestId('traffic-chart-timing-p95')).toHaveCount(1);
  await expect(card.getByTestId('traffic-chart-errors-4xx')).toHaveCount(1);
});

test('the traffic card asks its three questions as headings, and says a clean run in words (ADR: The Admin tab, as a product)', async ({
  page,
  request,
}) => {
  // Something to draw, and one request the site turns away, which is not a failure of the site's.
  for (const path of ['/api/vehicles?limit=5', '/api/facets', '/api/vehicles/not-a-vehicle']) {
    await request.get(`http://localhost:5210${path}`);
  }
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('traffic-card');
  await expect(card.getByTestId('traffic-stats')).toBeVisible({ timeout: 60_000 });
  // The one somebody opens the tab to find out comes first.
  await expect(card.getByRole('heading', { level: 3 })).toHaveText([
    'Did anything fail?',
    'How busy is it?',
    'How fast does it answer?',
  ]);
  // A flat line at zero reads as a chart that did not load, so zero is said in words.
  // The sentence and the block are made of the same count, so they are held to each other and
  // not to a number read a moment earlier: other tests share this API and its hour, and one of
  // them drawing a server error between two reads is not this card failing. A run with none,
  // which is what this suite is unless something else broke, says so in words and as good news.
  const fail = card.getByTestId('traffic-fail-line');
  const block = card.getByTestId('traffic-stat-errors');
  await expect(async () => {
    const clean = (await block.getAttribute('data-tone')) === 'good';
    await expect(block.locator('span').nth(1)).toHaveText(clean ? '0' : /^[1-9][\d,]*$/, {
      timeout: 2_000,
    });
    await expect(fail).toHaveText(
      clean
        ? 'goodNo server errors in the last hour.'
        : /^needs attention[\d,]+ server errors? in the last hour\.$/,
      { timeout: 2_000 }
    );
  }).toPass({ timeout: 30_000 });
  // A number on an axis is a number of something.
  await expect(card.getByTestId('traffic-chart-errors-unit')).toHaveText('errors / min');
  await expect(card.getByTestId('traffic-chart-requests-unit')).toHaveText('requests / min');
  await expect(card.getByTestId('traffic-chart-timing-unit')).toHaveText('ms');
});

test('no series line on the traffic card wears a status colour it has not earned (ADR: The Admin tab, as a product)', async ({
  page,
  request,
}) => {
  for (const path of ['/api/vehicles?limit=5', '/api/facets', '/api/vehicles/not-a-vehicle']) {
    await request.get(`http://localhost:5210${path}`);
  }
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('traffic-card');
  await expect(card.getByTestId('traffic-chart-timing')).toBeVisible({ timeout: 60_000 });
  // The status colours, read off the token sheet the page is running on.
  const status = await page.evaluate(() => {
    const probe = document.createElement('span');
    document.body.append(probe);
    const colours = ['--color-success', '--color-warning', '--color-danger'].map((token) => {
      probe.style.color = `var(${token})`;
      return getComputedStyle(probe).color;
    });
    probe.remove();
    return colours;
  });
  expect(new Set(status).size).toBe(3);
  const stroke = (testId: string) =>
    card
      .getByTestId(testId)
      .locator('path')
      .evaluate((path) => getComputedStyle(path).stroke);
  // The typical line and the slow line are two series, not a good one and a bad one.
  for (const line of ['traffic-chart-timing-p50', 'traffic-chart-timing-p95']) {
    expect(status).not.toContain(await stroke(line));
  }
  expect(await stroke('traffic-chart-timing-p50')).not.toBe(
    await stroke('traffic-chart-timing-p95')
  );
  // How busy is a series too, and a turned-away request is the visitor's, not something wrong with the site.
  expect(status).not.toContain(await stroke('traffic-chart-requests-requests'));
  expect(status).not.toContain(await stroke('traffic-chart-errors-4xx'));
  // A server error is something wrong, so its line is the one that may be the danger colour.
  expect(await stroke('traffic-chart-errors-5xx')).toBe(status[2]);
  // And nowhere on the tab is a chart's line a good one or a warning one.
  await expect(page.locator('svg [data-tone="good"], svg [data-tone="warn"]')).toHaveCount(0);
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
  // Signed out, the button says what it needs and takes the visitor to the
  // account view: starting a run is a write (ADR: The one write a stranger
  // can make, addendum), and a disabled button that reads like a call to
  // action is a broken button to the person tapping it.
  const run = card.getByTestId('proof-run');
  await expect(run).toBeEnabled();
  await expect(run).toHaveText('Sign in to run the proof');
  await run.click();
  await expect(page).toHaveURL(/view=account/);
  await signIn(page);
  await openTheYard(page, '/?view=admin');
  await expect(run).toHaveText(/Run the proof|Run it again/);
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
  // By kind is the default (1.0.3.12): the people band alone under Visitors
  // only; the store lines are the other view.
  await expect(card.getByTestId('activity-view-kind')).toHaveAttribute('aria-pressed', 'true');
  await expect(card.getByTestId('activity-band-people')).toHaveCount(1);
  await expect(card.getByTestId('activity-band-scanners')).toHaveCount(0);
  await expect(card.getByTestId('activity-legend')).toHaveCount(0);
  await card.getByTestId('activity-view-store').click();
  for (const store of stores.stores) {
    await expect(card.getByTestId(`activity-line-${store.key}`)).toHaveCount(1);
  }
  await card.getByTestId('activity-view-kind').click();
  await expect(card.getByTestId('activity-totals')).toContainText(/\d+ requests in the window/);
  // Visitors only is the default (1.0.3.11), and says what it left out; All
  // traffic counts the three kinds together, and the toggle fetches nothing.
  await expect(card.getByTestId('activity-who-people')).toHaveAttribute('aria-pressed', 'true');
  await expect(card.getByTestId('activity-left-out')).toContainText("the site's own reads");
  await card.getByTestId('activity-who-all').click();
  await expect(card.getByTestId('activity-who-all')).toHaveAttribute('aria-pressed', 'true');
  await expect(card.getByTestId('activity-totals')).toContainText(
    /\d+ people, \d+ scanners and crawlers/
  );
  await expect(card.getByTestId('activity-left-out')).toHaveCount(0);
  // All traffic stacks the three kinds, bottom to top, with a legend naming them.
  for (const kind of ['people', 'scanners', 'self']) {
    await expect(card.getByTestId(`activity-band-${kind}`)).toHaveCount(1);
  }
  await expect(card.getByTestId('activity-legend')).toContainText('Scanners and crawlers');
  await expect(card.getByTestId('activity-days-table')).toHaveCount(1);
  await expect(card.getByTestId('activity-graph')).toHaveAttribute('aria-label', /all traffic/);
  await card.getByTestId('activity-who-people').click();
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

  // And the key can be typed into the page, with no link carrying it: the
  // cards open at once and the key is remembered for the next plain visit.
  await page.getByTestId('admin-key-entry').fill('e2e-admin-key');
  await page.getByTestId('admin-key-submit').click();
  await expect(page.getByTestId('activity-visitors')).toBeVisible();
  await expect(page.getByTestId('kept-logs')).toBeVisible();
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('activity-visitors')).toBeVisible();
});
// #endregion remembered-key

// #region password-reset
test('a reset link minted behind the key sets a new password and signs the visitor in', async ({
  page,
  request,
}) => {
  const email = `reset-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.com`;
  const registered = await request.post('http://localhost:5210/api/auth/register', {
    data: { email, password: 'first password' },
  });
  expect(registered.ok()).toBe(true);

  // Without the key the endpoint does not exist; with it, a link for this site.
  expect(
    (
      await request.post('http://localhost:5210/api/admin/reset-links', { data: { email } })
    ).status()
  ).toBe(404);
  const minted = await request.post('http://localhost:5210/api/admin/reset-links', {
    data: { email },
    headers: { 'X-Admin-Key': 'e2e-admin-key' },
  });
  expect(minted.ok()).toBe(true);
  const { url } = (await minted.json()) as { url: string };
  const link = new URL(url);

  // The link opens the account view on the form; a new password signs the visitor in.
  await openTheYard(page, `/?view=account&reset=${link.searchParams.get('reset')}`);
  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
  await page.getByTestId('reset-password').fill('second password');
  // Wait on the reset response, as account.spec.ts waits on register and
  // login: setting the password costs a hash, and on a loaded machine the
  // round trip has overrun the default five seconds (1.0.0.127, take three).
  const reset = page.waitForResponse(
    (response) =>
      response.url().includes('/api/auth/reset') && response.request().method() === 'POST'
  );
  await page.getByTestId('reset-submit').click();
  const answered = await reset;
  expect(answered.status(), await answered.text()).toBe(200);
  await expect(page.getByRole('heading', { name: email })).toBeVisible();

  // And the new password is the password now.
  const again = await request.post('http://localhost:5210/api/auth/login', {
    data: { email, password: 'second password' },
  });
  expect(again.ok()).toBe(true);
  const old = await request.post('http://localhost:5210/api/auth/login', {
    data: { email, password: 'first password' },
  });
  expect(old.status()).toBe(401);
});
// #endregion password-reset

test('the Admin tab opens on tiles that answer four questions and go to the cards behind them', async ({
  page,
}) => {
  await openTheYard(page, '/?view=admin');
  const strip = page.getByTestId('stat-strip');
  await expect(strip.getByRole('button')).toHaveCount(8);
  // A tile is made of what a card has read, so it waits as long as the card does and then says the same thing.
  await expect(strip.getByTestId('tile-health')).toHaveAttribute('data-tone', 'good', {
    timeout: 45_000,
  });
  await expect(strip.getByTestId('tile-health')).toContainText(/\d+ of \d+ checks pass/);
  // The version is the build's own, "dev" on a developer's machine, and the line under it is how long it has been up.
  await expect(strip.getByTestId('tile-version')).toContainText(/, up \d+[dhm]/);
  await expect(strip.getByTestId('tile-memory')).toContainText(/\d+ of \d+ MB/, {
    timeout: 60_000,
  });
  await expect(strip.getByTestId('tile-speed')).toContainText('requests in the last hour');
  // The tone is a word as well as a colour.
  await expect(strip.getByTestId('tile-health')).toContainText('fine');
  // The four questions are headings, in the order somebody asks them, and every card is under one.
  for (const question of ['Is it up?', 'Is it fast?', 'Is it costing anything?', 'What broke?']) {
    await expect(page.getByRole('heading', { level: 2, name: question })).toBeVisible();
  }
  await expect(page.getByTestId('question-up').getByTestId('health-card')).toBeVisible();
  await expect(page.getByTestId('question-fast').getByTestId('traffic-card')).toBeVisible();
  await expect(page.getByTestId('question-cost').getByTestId('machines-card')).toBeVisible();
  await expect(page.getByTestId('question-broke').getByTestId('errors-card')).toBeVisible();
  // A tile goes to its question.
  await strip.getByTestId('tile-errors').click();
  await expect(page.getByRole('heading', { level: 2, name: 'What broke?' })).toBeInViewport();
  // What a card is and how to read it is one tap away and out of the way until then.
  const about = page.getByTestId('machines-card').locator('details').first();
  await expect(about).not.toHaveAttribute('open', '');
  await about.locator('summary').click();
  await expect(about).toContainText('The three machines under this site');
});

test('a public list reads a month back from the store, or says that nothing is kept here', async ({
  page,
  request,
}) => {
  // Something to keep: a registration, which is a statement on one store and an operation on the other.
  const email = `kept-canary-${Date.now()}@example.com`;
  const registered = await request.post('http://localhost:5210/api/auth/register', {
    data: { email, password: 'correct horse battery' },
  });
  expect(registered.status(), await registered.text()).toBe(200);
  const store = (await (await request.get('http://localhost:5210/api/admin/store')).json()) as {
    store: string;
  };
  const card = store.store === 'Azure Cosmos DB' ? 'store' : 'sql';
  const url = `http://localhost:5210/api/admin/kept?card=${card}&window=30d`;
  const first = (await (await request.get(url)).json()) as { kept: { available: boolean } };
  // The collector writes every two seconds here, so the entry is waited for
  // and not assumed. The SQLite run keeps nothing and the card says so.
  if (first.kept.available) {
    await expect
      .poll(
        async () =>
          JSON.stringify(
            ((await (await request.get(url)).json()) as { entries: unknown[] }).entries
          ),
        { timeout: 30_000 }
      )
      .toContain('POST /api/auth/register');
  }
  // No window and no card but the ones it names.
  expect(
    (await request.get('http://localhost:5210/api/admin/kept?card=secrets&window=30d')).status()
  ).toBe(400);

  await openTheYard(page, '/?view=admin');
  const shown = page.getByTestId(`${card}-card`);
  const line = shown.getByTestId(`kept-line-${card}`);
  await expect(line).toContainText('a roll empties');
  await shown.getByTestId(`kept-window-${card}-30d`).click();
  await expect(shown.getByTestId(`kept-window-${card}-30d`)).toHaveAttribute(
    'aria-pressed',
    'true'
  );
  if (first.kept.available) {
    await expect(line).toContainText(
      /The last 30 days, kept in Azure Cosmos DB for 35 days: (all|the newest) \d+/,
      { timeout: 30_000 }
    );
    await expect(shown).toContainText('POST /api/auth/register');
  } else {
    await expect(line).toContainText('Not kept here: no document store is configured');
  }
  // What is kept is what the ring serves: the address is nowhere in it.
  await expect(page.locator('body')).not.toContainText('kept-canary');
  await shown.getByTestId(`kept-window-${card}-now`).click();
  await expect(line).toContainText('a roll empties');
});

test('one window for every chart: the buttons over the tiles, on the traffic card and on the machines card are the same buttons', async ({
  page,
  request,
}) => {
  const wire = (await (
    await request.get('http://localhost:5210/api/admin/machines?window=30d')
  ).json()) as { history: { available: boolean } };
  await openTheYard(page, '/?view=admin');
  await expect(
    page.getByTestId('machines-card').getByTestId('machines-container-line')
  ).toBeVisible({
    timeout: 60_000,
  });
  await expect(page.getByTestId('strip-caption')).toHaveText(
    'The line under a tile is the last hour.'
  );
  // Every row of buttons offers the hour and the three windows, and a month is one of them.
  for (const row of ['strip-window', 'machines-window', 'machines-card-window']) {
    for (const option of ['1h', '24h', '7d', '30d']) {
      await expect(page.getByTestId(`${row}-${option}`)).toHaveCount(1);
    }
  }
  // Chosen on the machines card, pressed everywhere.
  await page.getByTestId('machines-card-window-30d').click();
  for (const row of ['strip-window', 'machines-window', 'machines-card-window']) {
    await expect(page.getByTestId(`${row}-30d`)).toHaveAttribute('aria-pressed', 'true');
  }
  // The tiles do not go back to waiting because a chart was asked for a month.
  await expect(page.getByTestId('tile-memory')).not.toHaveAttribute('data-tone', 'waiting');
  await expect(page.getByTestId('strip-caption')).toContainText(
    wire.history.available
      ? 'The line under a tile is the last 30 days'
      : 'The last 30 days is not kept here',
    { timeout: 30_000 }
  );
  await expect(page.getByTestId('traffic-card')).toContainText(
    wire.history.available ? 'in the last 30 days' : 'Last 30 days is not kept here',
    { timeout: 30_000 }
  );
  // And back, from over the tiles.
  await page.getByTestId('strip-window-1h').click();
  await expect(page.getByTestId('machines-card-window-1h')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('strip-caption')).toHaveText(
    'The line under a tile is the last hour.'
  );
});

test('the tests card shows every suite and every test the gate ran, failures first (ADR: The five-minute gate)', async ({
  page,
}) => {
  // The gate writes the real file after the suites pass, so this run cannot read its own; the
  // card is held to a fixed file in the gate's shape, one failure in it so the order shows.
  await page.route('**/api/admin/tests', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        version: '1.0.0.999',
        ranAt: '2026-09-21T17:40:00Z',
        gateSeconds: 400,
        checks: [{ name: 'Prettier', passed: true, seconds: 9 }],
        suites: [
          {
            id: 'xunit-sqlite',
            name: 'xUnit on SQLite',
            seconds: 12,
            passed: 2,
            failed: 1,
            skipped: 0,
            tests: [
              ['AuthTests', 'Sign in', 'p', 40],
              ['BidRulesTests', 'A bid under the reserve', 'p', 900],
              ['AuthTests', 'Lockout', 'f', 5],
            ],
          },
          {
            id: 'vitest',
            name: 'Vitest',
            seconds: 4,
            passed: 1,
            failed: 0,
            skipped: 0,
            tests: [['format.test.ts', 'a price in dollars', 'p', 3]],
          },
        ],
      }),
    })
  );
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('tests-card');
  await expect(card.getByTestId('tests-summary')).toContainText(
    '3 of 4 tests passed, 1 failed, for 1.0.0.999'
  );
  await expect(card.getByTestId('tests-checks')).toContainText('Prettier passed in 9 s');
  await expect(card.getByTestId('tests-suite-xunit-sqlite')).toContainText('xUnit on SQLite');
  // The verdict is a mark nobody has to read: a red cross beside the sentence when anything
  // failed, and a mark on every suite that says which one it was.
  await expect(card.locator('[data-verdict]').first()).toHaveAttribute('data-verdict', 'fail');
  await expect(
    card.getByTestId('tests-suite-xunit-sqlite').getByRole('img', { name: 'failed' })
  ).toBeVisible();
  await expect(
    card.getByTestId('tests-suite-vitest').getByRole('img', { name: 'passed' })
  ).toBeVisible();

  // A suite with a failure opens by itself, the failure on top; a green one opens on request.
  const failing = card.getByTestId('tests-list-xunit-sqlite');
  await expect(failing.locator('tbody tr').first()).toContainText('Lockout');
  await expect(failing.locator('tbody tr').first()).toContainText('failed');
  await expect(failing.locator('tbody tr').nth(1)).toContainText('A bid under the reserve');
  const green = card.getByTestId('tests-list-vitest');
  await expect(green.locator('tbody tr')).toHaveCount(0);
  await green.locator('summary').click();
  await expect(green.locator('tbody tr')).toHaveCount(1);

  // The filter narrows every suite by the words in a test's group or name.
  await card.getByTestId('tests-filter').fill('auth sign');
  await expect(failing.locator('tbody tr')).toHaveCount(1);
  await expect(failing.locator('summary')).toContainText('1 of 3 tests');
});
