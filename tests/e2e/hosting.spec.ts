import { expect, test } from '@playwright/test';

test('the Hosting section opens the hosting overview and the deployment ADRs', async ({ page }) => {
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Project documents' });
  await nav.getByRole('button', { name: 'Hosting overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Hosting' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Deployment strategy' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 2, name: 'ADR: Deployment strategy' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Edge deploy economics' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Edge deploy economics' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Linux over Windows' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Linux containers over Windows' })
  ).toBeVisible();
});
