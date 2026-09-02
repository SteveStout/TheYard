import { expect, test } from '@playwright/test';

test('the Best Practices section opens the overview and its decision records', async ({ page }) => {
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Project documents' });
  await nav.getByRole('button', { name: 'Best practices overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Best Practices' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Version in the footer' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Version in the footer' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Docs and testing' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Docs and testing' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Observability (Admin tab)' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Observability, the Admin tab' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: The sidebar' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: The sidebar' })
  ).toBeVisible();
});

test('the footer reports the running build', async ({ page }) => {
  await page.goto('/');
  const version = page.getByTestId('build-version');
  await expect(version).toBeVisible();
  await expect(version).toHaveText(/^(dev build|v\d+\.\d+\.\d+\.\d+)$/);
});
