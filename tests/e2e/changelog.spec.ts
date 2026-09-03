import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The Changelog section (ADR-012): one item, one file, one sentence per
 * version, newest first. Desktop only; the phone spec covers the drawer.
 */
test('the Changelog section opens the version list, newest first, and its record sits under Best Practices', async ({
  page,
}) => {
  await openTheYard(page);
  const nav = page.getByRole('navigation', { name: 'Project documents' });
  await nav.getByRole('button', { name: 'Version history' }).click();
  const doc = page.getByRole('dialog', { name: 'Changelog' });
  await expect(doc.getByRole('heading', { level: 1, name: 'Changelog' })).toBeVisible();

  const entries = doc.getByRole('listitem');
  await expect(entries.first()).toContainText(/^1\.0\.0\.\d+ \(\d{4}-\d{2}-\d{2}\): /);
  expect(await entries.count()).toBeGreaterThanOrEqual(15);
  await expect(entries.last()).toContainText(/^1\.0\.0\.1 \(2026-08-31\): /);
  await page.keyboard.press('Escape');
  await expect(doc).toBeHidden();

  // The records live in one collapsed index now (ADR-029); open it first.
  await nav.getByText('Decision Records', { exact: true }).click();
  await nav.getByRole('button', { name: 'ADR: The changelog' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: The changelog' })
  ).toBeVisible();
});
