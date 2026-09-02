import { expect, test } from '@playwright/test';

test('the CI/CD menu opens its overview and Hosting serves the Bicep file', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'CI/CD' }).click();
  await page.getByRole('menuitem', { name: 'CI/CD overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'CI/CD' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'CI/CD' }).click();
  await page.getByRole('menuitem', { name: 'ADR: The deploy pipeline' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: The deploy pipeline' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Hosting' }).click();
  await page.getByRole('menuitem', { name: 'Infrastructure (Bicep)' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'infra/main.bicep' })
  ).toBeVisible();
});
