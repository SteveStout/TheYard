import { expect, test } from '@playwright/test';

/**
 * Phone-sized viewport (iPhone-class, 375x812). Below 1024px the sidebar is a
 * drawer behind the header's hamburger (ADR-013), built from the same MENUS
 * record as the docked rail, and docs open full-screen. sidebar.spec proves
 * the rail; this file only proves the phone.
 */
test.use({ viewport: { width: 375, height: 812 } });

test('a phone gets one hamburger and no rail', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Menu' })).toBeVisible();
  await expect(page.getByTestId('side-rail')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Hosting' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'CI/CD' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Best Practices' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Changelog' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'About' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Admin', exact: true })).toBeHidden();
});

test('the drawer lists every menu, opens a doc full-screen, and closes on Escape', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  const drawer = page.getByRole('dialog', { name: 'Menu' });
  await expect(drawer).toBeVisible();
  for (const section of ['Hosting', 'CI/CD', 'Best Practices', 'Changelog', 'About']) {
    await expect(drawer.getByRole('heading', { name: section, exact: true })).toBeVisible();
  }
  await expect(drawer.getByRole('link', { name: "Steven's resume (PDF)" })).toHaveAttribute(
    'href',
    '/api/docs/resume'
  );
  await expect(drawer.getByRole('link', { name: 'GitHub repository' })).toHaveAttribute(
    'href',
    'https://github.com/SteveStout/TheYard'
  );

  await drawer.getByRole('button', { name: 'Hosting overview' }).click();
  await expect(drawer).toBeHidden();
  const doc = page.getByRole('dialog', { name: 'Hosting' });
  await expect(doc.getByRole('heading', { level: 1, name: 'Hosting' })).toBeVisible();
  const box = await doc.boundingBox();
  expect(box?.width).toBe(375);
  expect(box?.height).toBe(812);

  await page.keyboard.press('Escape');
  await expect(doc).toBeHidden();
});

test('every drawer row leads with an icon, stands at least 44px tall, and the changelog opens from its section', async ({
  page,
}) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  const drawer = page.getByRole('dialog', { name: 'Menu' });
  await expect(drawer).toBeVisible();

  // Every doc, the CI link, Admin, the resume and the repository: each row is
  // a button or a link carrying exactly one decorative (aria-hidden) svg.
  const rows = drawer.locator('button:not([aria-label="Close"]), a');
  const count = await rows.count();
  expect(count).toBeGreaterThanOrEqual(24);
  for (let i = 0; i < count; i += 1) {
    await expect(rows.nth(i).locator('svg[aria-hidden="true"]')).toHaveCount(1);
  }

  const changelogRow = drawer.getByRole('button', { name: 'Version history' });
  await changelogRow.scrollIntoViewIfNeeded();
  const rowBox = await changelogRow.boundingBox();
  expect(rowBox?.height).toBeGreaterThanOrEqual(44);
  const closeBox = await drawer.getByRole('button', { name: 'Close' }).boundingBox();
  expect(closeBox?.height).toBeGreaterThanOrEqual(44);
  expect(closeBox?.width).toBeGreaterThanOrEqual(44);

  await changelogRow.click();
  await expect(drawer).toBeHidden();
  await expect(
    page.getByRole('dialog', { name: 'Changelog' }).getByRole('heading', { level: 1, name: 'Changelog' })
  ).toBeVisible();
});

test('Admin is reachable from the drawer and the footer still renders', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  await page.getByRole('dialog', { name: 'Menu' }).getByRole('button', { name: 'Admin', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page).toHaveURL(/view=admin/);
  await expect(page.getByTestId('build-version')).toBeVisible();
});

test('the phone header has its own decision record, reachable from the drawer', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  await page.getByRole('dialog', { name: 'Menu' }).getByRole('button', { name: 'ADR: The phone header' }).click();
  await expect(
    page.getByRole('dialog', { name: 'ADR: The phone header' }).getByRole('heading', { level: 1, name: 'ADR: The phone header' })
  ).toBeVisible();
});
