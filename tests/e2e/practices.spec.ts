import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

test('the Best Practices section opens the overview and its decision records', async ({ page }) => {
  await openTheYard(page);
  const nav = page.getByRole('navigation', { name: 'Project documents' });
  await nav.getByRole('button', { name: 'Best practices overview' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'Best Practices' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  // The records live in one collapsed index now (ADR-029); open it first.
  await nav.getByText('Decision Records', { exact: true }).click();
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
    page
      .getByRole('dialog')
      .getByRole('heading', { level: 1, name: 'ADR: Observability, the Admin tab' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: The sidebar' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: The sidebar' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  // The live-sample record shows the expander's own code, read from this build.
  await nav.getByRole('button', { name: 'ADR: Live code samples' }).click();
  const live = page.getByRole('dialog', { name: 'ADR: Live code samples' });
  await expect(
    live.getByRole('heading', { level: 1, name: 'ADR: Live code samples' })
  ).toBeVisible();
  await expect(live.locator('pre code').first()).toContainText('IsAllowedPath');
  await expect(live.locator('em').filter({ hasText: 'Sample unavailable' })).toHaveCount(0);
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Cache headers' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: Cache headers' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: The palette' }).click();
  const palette = page.getByRole('dialog', { name: 'ADR: The palette' });
  await expect(palette.getByRole('heading', { level: 1, name: 'ADR: The palette' })).toBeVisible();
  await expect(palette.locator('pre code').first()).toContainText('--color-bg: #e9e6e7');
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: The staff review' }).click();
  await expect(
    page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: The staff review' })
  ).toBeVisible();
  await page.keyboard.press('Escape');

  // The two records for a new developer (ADR-018, ADR-019) render their first
  // sample from the build and no fallback note anywhere.
  await nav.getByRole('button', { name: 'ADR: Program.cs, explained' }).click();
  const program = page.getByRole('dialog', { name: 'ADR: Program.cs, explained' });
  await expect(
    program.getByRole('heading', { level: 1, name: 'ADR: Program.cs, explained' })
  ).toBeVisible();
  await expect(program.locator('pre code').first()).toContainText('FindUpward');
  await expect(program.locator('em').filter({ hasText: 'Sample unavailable' })).toHaveCount(0);
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: The React configuration, explained' }).click();
  const react = page.getByRole('dialog', { name: 'ADR: The React configuration, explained' });
  await expect(
    react.getByRole('heading', { level: 1, name: 'ADR: The React configuration, explained' })
  ).toBeVisible();
  await expect(react.locator('pre code').first()).toContainText('"build": "tsc -b && vite build"');
  await expect(react.locator('em').filter({ hasText: 'Sample unavailable' })).toHaveCount(0);
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: Diagram pages' }).click();
  const diagrams = page.getByRole('dialog', { name: 'ADR: Diagram pages' });
  await expect(
    diagrams.getByRole('heading', { level: 1, name: 'ADR: Every diagram opens on its own page' })
  ).toBeVisible();
  await expect(diagrams.locator('pre code').first()).toContainText('/api/docs/diagrams/{name}');
  await expect(diagrams.locator('em').filter({ hasText: 'Sample unavailable' })).toHaveCount(0);
  await page.keyboard.press('Escape');

  await nav.getByRole('button', { name: 'ADR: The tests, explained' }).click();
  const tests = page.getByRole('dialog', { name: 'ADR: The tests, explained' });
  await expect(
    tests.getByRole('heading', { level: 1, name: 'ADR: The tests, explained' })
  ).toBeVisible();
  await expect(tests.locator('pre code').first()).toContainText('Increments_are_tiered');
  await expect(tests.locator('em').filter({ hasText: 'Sample unavailable' })).toHaveCount(0);
});

test('the footer reports the running build', async ({ page }) => {
  await openTheYard(page);
  const version = page.getByTestId('build-version');
  await expect(version).toBeVisible();
  await expect(version).toHaveText(/^(dev build|v\d+\.\d+\.\d+\.\d+)$/);
});
