import { expect, test } from '@playwright/test';

test('the Admin tab shows the running system reporting on itself', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Admin' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.getByTestId('health-card')).toContainText('healthy');
  await expect(page.getByTestId('errors-card')).toBeVisible();
  await expect(page.getByTestId('azure-card')).toBeVisible();
  await page.getByRole('button', { name: 'Back to inventory' }).click();
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
});

test('?view=admin deep-links straight to the Admin tab', async ({ page }) => {
  await page.goto('/?view=admin');
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
});
