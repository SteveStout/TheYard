import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { bidTheMinimum } from './bidding';

/**
 * Smokes over the real stack: Vite → proxy → .NET API → 100k synthetic
 * dataset. Bids mutate shared API state, so the suite runs serially and
 * resets bid state around the bidding test.
 */

test.beforeEach(async ({ request }) => {
  // Clearing bids needs an account now (ADR: Accounts and per-user bids). The
  // reset still clears the room for everybody, which is the whole reason this
  // hook exists, so it registers a throwaway account to do it with. The worker
  // request fixture keeps its cookies between calls, so the register and the
  // delete are the same caller.
  await request.post('http://localhost:5210/api/auth/register', {
    data: {
      email: `smoke-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.com`,
      password: 'correct horse',
    },
  });
  await request.delete('http://localhost:5210/api/bids');
});

test('landing page shows the top 100 of the full dataset', async ({ page }) => {
  await openTheYard(page);
  await expect(page.getByTestId('result-count')).toHaveText(/Showing 100 of 100,000 vehicles/);
  await expect(page.locator('article')).toHaveCount(100);
  // Fifty photographs do the work of a hundred thousand, so two cards in the
  // same row can carry the same picture. Said once, above the grid, because
  // without it that reads as a rendering fault.
  await expect(page.getByText('Photographs are stock')).toBeVisible();
});

test('filtering updates the URL and a filtered URL restores the view', async ({ page }) => {
  await openTheYard(page);
  await page.getByLabel('Search vehicles').fill('bronco');
  await expect(page).toHaveURL(/q=bronco/);

  await openTheYard(page, '/?make=Kia&status=upcoming&sort=price-asc');
  await expect(page.locator('select').nth(1)).toHaveValue('Kia');
  await expect(page.getByTestId('result-count')).toHaveText(/of [\d,]+ vehicles/);
});

test('load more appends the next page', async ({ page }) => {
  await openTheYard(page);
  await expect(page.locator('article')).toHaveCount(100);
  await page.getByRole('button', { name: 'Load more vehicles' }).click();
  await expect(page.locator('article')).toHaveCount(200);
});

// #region get-navigation
// Locators are by role and accessible name, so the test sees what a visitor
// sees; the URL is asserted at every step because the address bar is the
// app's state, and page.goBack() is the real Back button.
test('tile clicks are GET navigation: URL updates, Back works, deep links restore', async ({
  page,
}) => {
  await openTheYard(page, '/?status=live&sort=most-bids');
  await page.waitForSelector('article');

  // Opening a tile pushes a history entry with ?vehicle={id}.
  await page.locator('article h3 button').first().click();
  await expect(page).toHaveURL(/vehicle=/);
  await expect(page.getByText('Specifications')).toBeVisible();
  // The catalogue is synthetic and the photographs are vendored stock chosen
  // for the body style, so the page says so where somebody looking at a Tesla
  // listing with another manufacturer's SUV in it would otherwise draw their
  // own conclusion.
  await expect(page.getByText('Not photographs of this vehicle.')).toBeVisible();
  const detailUrl = page.url();

  // The browser's Back button returns to the filtered list.
  await page.goBack();
  await expect(page).not.toHaveURL(/vehicle=/);
  await expect(page).toHaveURL(/status=live/);
  await expect(page.locator('article').first()).toBeVisible();

  // A cold load of the detail URL deep-links straight into the detail view.
  await openTheYard(page, detailUrl);
  await expect(page.getByText('Specifications')).toBeVisible();

  // The in-app back control from a deep link swaps to the list without exiting.
  await page.getByRole('button', { name: 'Back to inventory' }).click();
  await expect(page).not.toHaveURL(/vehicle=/);
  await expect(page.locator('article').first()).toBeVisible();
});
// #endregion get-navigation

test('the About section shows the README in-app and links the résumé PDF', async ({ page }) => {
  await openTheYard(page);
  const nav = page.getByRole('navigation', { name: 'Project documents' });
  await nav.getByRole('button', { name: 'Project README' }).click();
  await expect(page.getByRole('dialog').getByRole('heading', { name: /TheYard/ })).toBeVisible();
  await page.getByRole('dialog').getByLabel('Close').click();
  await expect(page.getByRole('dialog')).toBeHidden();

  await nav.getByRole('button', { name: 'Data flow diagram' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Data Flow' })
  ).toBeVisible();
  const openFlow = page
    .getByRole('dialog')
    .getByRole('link', { name: 'Open the data flow diagram in a new page' });
  await expect(openFlow).toHaveAttribute('target', '_blank');
  await expect(openFlow).toHaveAttribute('href', '/api/docs/diagrams/dataflow');
  await page.keyboard.press('Escape');

  // Architecture and style are the App Architecture section's own pages (ADR-022).
  await nav.getByRole('button', { name: 'Architecture overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'App Architecture' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'Coding and comments' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Coding and Commenting Style' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'Project structure' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 2, name: 'TheYard.Data' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  const resume = await page.request.get('/api/docs/resume');
  expect(resume.headers()['content-type']).toContain('application/pdf');

  await expect(nav.getByRole('link', { name: 'GitHub repository' })).toHaveAttribute(
    'href',
    'https://github.com/SteveStout/TheYard'
  );
});

test('a transient API failure shows the stale banner and Retry recovers', async ({ page }) => {
  await openTheYard(page);
  await page.waitForSelector('article');

  // Simulate the API dropping out mid-session.
  await page.route('**/api/vehicles*', (route) => route.abort());
  await page.getByLabel('Search vehicles').fill('bronco');
  await expect(page.getByText(/Couldn't update results/)).toBeVisible({ timeout: 10_000 });
  // The previous list stays visible instead of blanking.
  await expect(page.locator('article')).toHaveCount(100);

  // The API comes back; Retry refetches and clears the banner.
  await page.unroute('**/api/vehicles*');
  await page.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByText(/Couldn't update results/)).toHaveCount(0);
  await expect(page.getByTestId('result-count')).toHaveText(/of [\d,]+ vehicles/);
});

test('a bid round-trips through the API and survives a reload', async ({ page }) => {
  test.setTimeout(90_000);
  // Signs in, opens a live vehicle and bids the minimum, reading it again if the
  // room raised the price in between. This test used to do that itself, once,
  // with no retry, and pass: market.spec's reset was clearing the simulated room
  // globally, so there was nothing bidding against it. Scoping that reset to its
  // own user took the crutch away and this failed twice in a row
  // (ADR: Reset is one person's start-over).
  await bidTheMinimum(page);

  // Server-side state: still the high bidder after a full reload.
  await page.reload();
  await expect(page.getByRole('button', { name: /Reset bids/ })).toBeVisible();

  // Reset clears it (accept the confirm dialog).
  page.on('dialog', (dialog) => void dialog.accept());
  await page.getByRole('button', { name: /Reset bids/ }).click();
  await expect(page.getByRole('button', { name: /Reset bids/ })).toHaveCount(0);
});
