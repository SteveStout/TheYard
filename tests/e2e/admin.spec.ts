import { expect, test } from '@playwright/test';

test('the Admin tab shows the running system reporting on itself', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Project documents' }).getByRole('button', { name: 'Admin', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.getByTestId('health-card')).toContainText('healthy');
  // Every check shows how long it took (ADR-010, second pass).
  await expect(page.getByTestId('check-duration').first()).toHaveText(/^\d+ ms$/);
  expect(await page.getByTestId('check-duration').count()).toBeGreaterThanOrEqual(3);
  await expect(page.getByTestId('errors-card')).toBeVisible();
  await expect(page.getByTestId('azure-card')).toBeVisible();
  await page.getByRole('button', { name: 'Back to inventory' }).click();
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
});

test('?view=admin deep-links straight to the Admin tab', async ({ page }) => {
  await page.goto('/?view=admin');
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
});
