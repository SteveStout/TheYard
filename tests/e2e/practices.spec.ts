import { expect, test } from '@playwright/test';

test('the Best Practices menu opens the overview and the versioning ADR', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Best Practices' }).click();
  await page.getByRole('menuitem', { name: 'Best practices overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Best Practices' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Best Practices' }).click();
  await page.getByRole('menuitem', { name: 'ADR: Version in the footer' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Version in the footer' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Best Practices' }).click();
  await page.getByRole('menuitem', { name: 'ADR: Docs and testing' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Docs and testing' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Best Practices' }).click();
  await page.getByRole('menuitem', { name: 'ADR: Observability (Admin tab)' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Observability, the Admin tab' })
  ).toBeVisible();
});

test('the footer reports the running build', async ({ page }) => {
  await page.goto('/');
  const version = page.getByTestId('build-version');
  await expect(version).toBeVisible();
  await expect(version).toHaveText(/^(dev build|v\d+\.\d+\.\d+\.\d+)$/);
});
