import { expect, test } from '@playwright/test';

test('the Hosting menu opens the hosting overview and the deployment ADR', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Hosting' }).click();
  await page.getByRole('menuitem', { name: 'Hosting overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Hosting' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Hosting' }).click();
  await page.getByRole('menuitem', { name: 'ADR: Deployment strategy' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 2, name: 'ADR: Deployment strategy' })
  ).toBeVisible();
});
