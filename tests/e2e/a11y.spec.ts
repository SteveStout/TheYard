import { expect, test } from '@playwright/test';

// The keyboard path, walked (ADR-026). Focus is the kind of behaviour a
// refactor breaks without breaking anything visible, which is what makes it
// worth a test rather than a note.

test('the first Tab reaches the skip link, and it jumps past the rail', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();

  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'Skip to content' });
  await expect(skip).toBeFocused();
  // Off-screen until it holds focus, on-screen once it does. If this fails the
  // link exists but nobody can see where they are.
  await expect(skip).toBeInViewport();

  await page.keyboard.press('Enter');
  await expect(page.locator('#main-content')).toBeFocused();
});

test('opening a vehicle, and coming back, moves focus to the view that changed', async ({ page }) => {
  await page.goto('/');
  const firstTile = page.locator('article h3 button').first();
  await expect(firstTile).toBeVisible();

  await firstTile.click();
  await expect(page.getByRole('button', { name: 'Back to inventory' })).toBeVisible();
  // Without this, focus is on <body>: the tile it was on no longer exists.
  await expect(page.locator('#main-content')).toBeFocused();

  await page.getByRole('button', { name: 'Back to inventory' }).click();
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
  await expect(page.locator('#main-content')).toBeFocused();
});

test('the Admin tab takes focus too', async ({ page }) => {
  await page.goto('/');
  await page
    .getByRole('navigation', { name: 'Project documents' })
    .getByRole('button', { name: 'Admin', exact: true })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.locator('#main-content')).toBeFocused();
});

test('the live region names the view, and the filter bar keeps the count', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByTestId('view-announcement')).toHaveText('Vehicle inventory');
  await expect(page.getByTestId('result-count')).toContainText('vehicles');

  await page.locator('article h3 button').first().click();
  await expect(page.getByRole('button', { name: 'Back to inventory' })).toBeVisible();
  await expect(page.getByTestId('view-announcement')).toContainText('vehicle detail');
});
