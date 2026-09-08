import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

test('the Admin tab shows the running system reporting on itself', async ({ page }) => {
  await openTheYard(page);
  await page
    .getByRole('navigation', { name: 'Project documents' })
    .getByRole('button', { name: 'Admin', exact: true })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.getByTestId('health-card')).toContainText('healthy');
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
  // The SQL card on a relational container, the operations card on the
  // document one: the same page, whichever store it is on (ADR: What the store
  // is actually doing).
  await expect(page.getByTestId('sql-card').or(page.getByTestId('store-card'))).toBeVisible();
  await expect(page.getByTestId('log-card')).toContainText('Category');
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
}) => {
  // A local run has no peer configured, which is the first of the four ways the
  // peer column can be empty, and the card has to read as a card rather than
  // as an error: this container's column filled in, the other's saying why not,
  // and every other card on the page untouched.
  await openTheYard(page, '/?view=admin');
  const card = page.getByTestId('backends-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('Backends, side by side');
  await expect(card).toContainText('No peer is configured on this container');
  await expect(card).toContainText(/This site \((SQLite|Azure SQL Database|Azure Cosmos DB)\)/);
  // The label and the number are neighbouring cells, and a cell boundary is
  // no whitespace at all in the text Playwright reads, so \s* rather than \s+.
  await expect(card).toContainText(/Cold start, process start to ready\s*\d+ ms/);
  await expect(card).toContainText(/Catalogue load\s*\d+ ms/);
  await expect(card).toContainText('Bid write');
  await expect(card).toContainText('Sign in');
  await expect(page.getByTestId('health-card')).toContainText('healthy');
  await expect(page.getByTestId('sql-card').or(page.getByTestId('store-card'))).toBeVisible();
});
// #endregion backends-card
