import { expect, test } from '@playwright/test';

/**
 * Smokes over the real stack: Vite → proxy → .NET API → 100k synthetic
 * dataset. Bids mutate shared API state, so the suite runs serially and
 * resets bid state around the bidding test.
 */

test.beforeEach(async ({ request }) => {
  await request.delete('http://localhost:5210/api/bids');
});

test('landing page shows the top 100 of the full dataset', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('status')).toHaveText(/Showing 100 of 100,000 vehicles/);
  await expect(page.locator('article')).toHaveCount(100);
});

test('filtering updates the URL and a filtered URL restores the view', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Search vehicles').fill('bronco');
  await expect(page).toHaveURL(/q=bronco/);

  await page.goto('/?make=Kia&status=upcoming&sort=price-asc');
  await expect(page.locator('select').nth(1)).toHaveValue('Kia');
  await expect(page.getByRole('status')).toHaveText(/of [\d,]+ vehicles/);
});

test('load more appends the next page', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('article')).toHaveCount(100);
  await page.getByRole('button', { name: 'Load more vehicles' }).click();
  await expect(page.locator('article')).toHaveCount(200);
});

test('tile clicks are GET navigation: URL updates, Back works, deep links restore', async ({ page }) => {
  await page.goto('/?status=live&sort=most-bids');
  await page.waitForSelector('article');

  // Opening a tile pushes a history entry with ?vehicle={id}.
  await page.locator('article h3 button').first().click();
  await expect(page).toHaveURL(/vehicle=/);
  await expect(page.getByText('Specifications')).toBeVisible();
  const detailUrl = page.url();

  // The browser's Back button returns to the filtered list.
  await page.goBack();
  await expect(page).not.toHaveURL(/vehicle=/);
  await expect(page).toHaveURL(/status=live/);
  await expect(page.locator('article').first()).toBeVisible();

  // A cold load of the detail URL deep-links straight into the detail view.
  await page.goto(detailUrl);
  await expect(page.getByText('Specifications')).toBeVisible();

  // The in-app back control from a deep link swaps to the list without exiting.
  await page.getByRole('button', { name: 'Back to inventory' }).click();
  await expect(page).not.toHaveURL(/vehicle=/);
  await expect(page.locator('article').first()).toBeVisible();
});

test('the About menu shows the README in-app and links the résumé PDF', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'About' }).click();
  await page.getByRole('menuitem', { name: 'Project README' }).click();
  await expect(page.getByRole('dialog').getByRole('heading', { name: /The Block/ })).toBeVisible();
  await page.getByRole('dialog').getByLabel('Close').click();
  await expect(page.getByRole('dialog')).toBeHidden();

  await page.getByRole('button', { name: 'About' }).click();
  await page.getByRole('menuitem', { name: 'Data flow diagram' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Data Flow' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'About' }).click();
  await page.getByRole('menuitem', { name: 'Project structure' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 2, name: 'TheBlock.Data' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  const resume = await page.request.get('/api/docs/resume');
  expect(resume.headers()['content-type']).toContain('application/pdf');

  await page.getByRole('button', { name: 'About' }).click();
  await expect(page.getByRole('menuitem', { name: 'GitHub repository' })).toHaveAttribute(
    'href',
    'https://github.com/SteveStout/CodingChallengeOpenLane'
  );
});

test('a transient API failure shows the stale banner and Retry recovers', async ({ page }) => {
  await page.goto('/');
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
  await expect(page.getByRole('status')).toHaveText(/of [\d,]+ vehicles/);
});

test('a bid round-trips through the API and survives a reload', async ({ page }) => {
  // Sort by most bids: the top card is live with a window ending hours or
  // days out (the default sort's first card can expire within seconds).
  await page.goto('/?status=live&sort=most-bids');
  await page.waitForSelector('article');

  await page.locator('article h3 button').first().click();
  await expect(page.getByText('Specifications')).toBeVisible();
  const min = await page.locator('#bid-amount').getAttribute('placeholder');
  await page.locator('#bid-amount').fill(min!);
  await page.getByRole('button', { name: 'Place bid' }).click();
  // A minimum bid is normally accepted — but when min_next_bid crosses the
  // vehicle's buy-now price, the rules award an instant win. Both are valid.
  await expect(page.getByText(/You're the high bidder|You bought this vehicle/)).toBeVisible();

  // Server-side state: still the high bidder after a full reload.
  await page.reload();
  await expect(page.getByRole('button', { name: /Reset bids/ })).toBeVisible();

  // Reset clears it (accept the confirm dialog).
  page.on('dialog', (dialog) => void dialog.accept());
  await page.getByRole('button', { name: /Reset bids/ }).click();
  await expect(page.getByRole('button', { name: /Reset bids/ })).toHaveCount(0);
});
