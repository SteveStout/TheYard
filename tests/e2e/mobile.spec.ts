import { expect, test } from '@playwright/test';

/**
 * Phone-sized viewport (iPhone-class, 375x812). Below 640px the four header
 * dropdowns and the Admin button give way to one hamburger sheet built from
 * the same MENUS record, and docs open full-screen. The desktop specs keep
 * proving the dropdowns; this file only proves the phone.
 */
test.use({ viewport: { width: 375, height: 812 } });

test('a phone gets one hamburger instead of four dropdowns', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Menu' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Hosting' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'CI/CD' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Best Practices' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'About' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Admin', exact: true })).toBeHidden();
});

test('the sheet lists every menu, opens a doc full-screen, and closes on Escape', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  const sheet = page.getByRole('dialog', { name: 'Menu' });
  await expect(sheet).toBeVisible();
  for (const section of ['Hosting', 'CI/CD', 'Best Practices', 'About']) {
    await expect(sheet.getByRole('heading', { name: section, exact: true })).toBeVisible();
  }
  await expect(sheet.getByRole('link', { name: "Steven's resume (PDF)" })).toHaveAttribute(
    'href',
    '/api/docs/resume'
  );
  await expect(sheet.getByRole('link', { name: 'GitHub repository' })).toHaveAttribute(
    'href',
    'https://github.com/SteveStout/TheYard'
  );

  await sheet.getByRole('button', { name: 'Hosting overview' }).click();
  await expect(sheet).toBeHidden();
  const doc = page.getByRole('dialog', { name: 'Hosting' });
  await expect(doc.getByRole('heading', { level: 1, name: 'Hosting' })).toBeVisible();
  const box = await doc.boundingBox();
  expect(box?.width).toBe(375);
  expect(box?.height).toBe(812);

  await page.keyboard.press('Escape');
  await expect(doc).toBeHidden();
});

test('Admin is reachable from the sheet and the footer still renders', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  await page.getByRole('dialog', { name: 'Menu' }).getByRole('button', { name: 'Admin', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page).toHaveURL(/view=admin/);
  await expect(page.getByTestId('build-version')).toBeVisible();
});

test('the phone header has its own decision record, reachable from the sheet', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  await page.getByRole('dialog', { name: 'Menu' }).getByRole('button', { name: 'ADR: The phone header' }).click();
  await expect(
    page.getByRole('dialog', { name: 'ADR: The phone header' }).getByRole('heading', { level: 1, name: 'ADR: The phone header' })
  ).toBeVisible();
});
