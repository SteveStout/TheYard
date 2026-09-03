import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

test('the CI/CD section opens its overview and Hosting serves the Bicep file', async ({ page }) => {
  await openTheYard(page);
  const nav = page.getByRole('navigation', { name: 'Project documents' });
  await nav.getByRole('button', { name: 'CI/CD overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'CI/CD' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  // The records live in one collapsed index now (ADR-029); open it first.
  await nav.getByText('Decision Records', { exact: true }).click();
  await nav.getByRole('button', { name: 'ADR: The deploy pipeline' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: The deploy pipeline' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await expect(nav.getByRole('link', { name: 'CI runs on GitHub' })).toHaveAttribute(
    'href',
    'https://github.com/SteveStout/TheYard/actions'
  );

  await nav.getByRole('button', { name: 'Infrastructure (Bicep)' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'infra/main.bicep' })
  ).toBeVisible();
});
