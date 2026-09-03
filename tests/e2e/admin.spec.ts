import { expect, test } from '@playwright/test';

test('the Admin tab shows the running system reporting on itself', async ({ page }) => {
  await page.goto('/');
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
  await expect(page.getByTestId('sql-card')).toBeVisible();
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

  await page.goto('/?view=admin');
  await expect(page.getByTestId('errors-card')).toContainText(marker);
});

test('?view=admin deep-links straight to the Admin tab', async ({ page }) => {
  await page.goto('/?view=admin');
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

  await page.goto('/?view=admin');
  const card = page.getByTestId('sql-card');
  await expect(card).toBeVisible();
  // A statement, with the request that caused it and a parameter described.
  await expect(card).toContainText('AspNetUsers');
  await expect(card).toContainText('POST /api/auth/register');
  await expect(card).toContainText(/@\w+ \w+/);
  // The address itself is nowhere on the page.
  await expect(page.locator('body')).not.toContainText(email);
  await expect(page.locator('body')).not.toContainText('sql-canary');
});
