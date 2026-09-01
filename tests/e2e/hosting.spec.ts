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
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Hosting' }).click();
  await page.getByRole('menuitem', { name: 'ADR: Edge deploy economics' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Edge deploy economics' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Hosting' }).click();
  await page.getByRole('menuitem', { name: 'ADR: Linux over Windows' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Linux containers over Windows' })
  ).toBeVisible();
});
