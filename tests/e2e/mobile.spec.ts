import { expect, test } from '@playwright/test';
import { openTheYard, openAllSections, openSection } from './app';

/**
 * Phone-sized viewport (iPhone-class, 375x812). Below 1024px the sidebar is a
 * drawer behind the header's hamburger (ADR-013), built from the same MENUS
 * record as the docked rail, and docs open full-screen. sidebar.spec proves
 * the rail; this file only proves the phone.
 */
test.use({ viewport: { width: 375, height: 812 } });

test('a phone gets one hamburger and no rail', async ({ page }) => {
  await openTheYard(page);
  await expect(page.getByRole('button', { name: 'Menu' })).toBeVisible();
  await expect(page.getByTestId('side-rail')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Hosting' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'CI/CD' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Best Practices' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Changelog' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'About' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Admin', exact: true })).toBeHidden();
});

test('the drawer lists every menu, opens a doc full-screen, and closes on Escape', async ({
  page,
}) => {
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const drawer = page.getByRole('dialog', { name: 'Menu' });
  await expect(drawer).toBeVisible();
  for (const section of [
    'App Architecture',
    'Performance',
    'Style',
    'Hosting',
    'Built with AI',
    'CI/CD',
    'Best Practices',
    'Changelog',
    'About',
    'Author',
  ]) {
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

  await openSection(drawer, 'Hosting');
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
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const drawer = page.getByRole('dialog', { name: 'Menu' });
  await expect(drawer).toBeVisible();

  // Every doc, the CI link, Admin, the resume and the repository: each row is
  // a button or a link carrying exactly one decorative (aria-hidden) svg.
  // Every section is closed on arrival since 1.0.0.135, so open them all: this
  // test is about what a row looks like, not about how many are showing.
  await openAllSections(drawer);
  const rows = drawer.locator('button:not([aria-label="Close"]), a');
  const count = await rows.count();
  expect(count).toBeGreaterThanOrEqual(24);
  // Read as one step over every row, not one round trip per row: seventy-odd sequential reads overran
  // the test's minute on a loaded machine (1.0.0.176's takes), with every row correct.
  await expect(async () => {
    const icons = await rows.evaluateAll((list) =>
      list.map((row) => row.querySelectorAll('svg[aria-hidden="true"]').length)
    );
    expect(icons).toHaveLength(count);
    expect(icons.filter((n) => n !== 1)).toEqual([]);
  }).toPass({ timeout: 20_000 });

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
    page
      .getByRole('dialog', { name: 'Changelog' })
      .getByRole('heading', { level: 1, name: 'Changelog' })
  ).toBeVisible();
});

test('Admin is reachable from the drawer and the footer still renders', async ({ page }) => {
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  await page
    .getByRole('dialog', { name: 'Menu' })
    .getByRole('button', { name: 'Admin', exact: true })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page).toHaveURL(/view=admin/);
  // The timed checks fit a phone row too (ADR-010, second pass).
  await expect(page.getByTestId('health-card')).toContainText('healthy');
  await expect(page.getByTestId('check-duration').first()).toHaveText(/^\d+ ms$/);
  await expect(page.getByTestId('build-version')).toBeVisible();
  // The header brand is the way home from Admin on a phone too (ADR-017).
  await page
    .getByRole('banner')
    .getByRole('button', { name: /The Yard/ })
    .click();
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
});

test('the phone header has its own decision record, reachable from the drawer', async ({
  page,
}) => {
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  // The records live in one collapsed index now (ADR-029); open it first.
  await page
    .getByRole('dialog', { name: 'Menu' })
    .locator('summary')
    .filter({ hasText: 'Decision Records' })
    .first()
    .click();
  await page
    .getByRole('dialog', { name: 'Menu' })
    .getByRole('button', { name: 'ADR: The phone header' })
    .click();
  await expect(
    page
      .getByRole('dialog', { name: 'ADR: The phone header' })
      .getByRole('heading', { level: 1, name: 'ADR: The phone header' })
  ).toBeVisible();
});

test('the Admin tiles are two to a row on a phone and nothing on the tab is wider than the phone', async ({
  page,
}) => {
  await openTheYard(page, '/?view=admin');
  const strip = page.getByTestId('stat-strip');
  await expect(strip.getByTestId('tile-health')).toHaveAttribute('data-tone', 'good', {
    timeout: 45_000,
  });
  const first = await strip.getByTestId('tile-version').boundingBox();
  const second = await strip.getByTestId('tile-health').boundingBox();
  const third = await strip.getByTestId('tile-pages').boundingBox();
  // Two on the first row, side by side and the same size, and the third under the first.
  expect(first?.y).toBe(second?.y);
  expect(first?.width).toBe(second?.width);
  expect(first?.height).toBe(second?.height);
  expect(third?.x).toBe(first?.x);
  expect(third?.y ?? 0).toBeGreaterThan(first?.y ?? 0);
  expect((second?.x ?? 0) + (second?.width ?? 0)).toBeLessThanOrEqual(375);
  // The traffic card's four numbers wear the same look and keep the same rule: two to a row.
  const stats = page.getByTestId('traffic-stats');
  await expect(stats).toBeVisible({ timeout: 60_000 });
  const requests = await stats.getByTestId('traffic-stat-requests').boundingBox();
  const typical = await stats.getByTestId('traffic-stat-typical').boundingBox();
  const slow = await stats.getByTestId('traffic-stat-slow').boundingBox();
  expect(requests?.y).toBe(typical?.y);
  expect(requests?.width).toBe(typical?.width);
  expect(slow?.x).toBe(requests?.x);
  expect(slow?.y ?? 0).toBeGreaterThan(requests?.y ?? 0);
  expect((typical?.x ?? 0) + (typical?.width ?? 0)).toBeLessThanOrEqual(375);
  // The machines have read by now, so the widest things on the tab are drawn: charts and tables scroll inside their cards.
  await expect(page.getByTestId('machines-card')).toBeVisible({ timeout: 60_000 });
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(overflow).toBe(0);
});

test('the Admin and Account pills are a thumb tall on a phone', async ({ page }) => {
  // 44 px is the touch target the intro strip's pills already meet; on the desk
  // the same pills stay at 34 so the Admin rows stay dense (Steve, 2026-09-22).
  await openTheYard(page, '/?view=admin');
  const strip = page.getByTestId('stat-strip');
  await expect(strip.getByTestId('tile-health')).toHaveAttribute('data-tone', 'good', {
    timeout: 45_000,
  });
  for (const pill of [
    page.getByRole('button', { name: 'Back to inventory' }),
    page.getByTestId('strip-window-1h'),
  ]) {
    await expect(async () => {
      const box = await pill.boundingBox();
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
    }).toPass();
  }
  await openTheYard(page, '/?view=account');
  await expect(async () => {
    const box = await page.getByRole('button', { name: 'Back to inventory' }).boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }).toPass();
});

test('the first screen on a phone says what this is, who built it and where the resume is (ADR: The glass look)', async ({
  page,
}) => {
  await openTheYard(page);
  const strip = page.getByTestId('intro-strip');
  await expect(strip).toBeVisible();
  await expect(strip).toContainText('Steven Stout');
  // Brand, purpose and a way to the resume, all inside the first 812 pixels, without a scroll.
  const brand = await page
    .getByRole('button', { name: /The Yard/ })
    .first()
    .boundingBox();
  const sentence = await strip.locator('p').boundingBox();
  const resume = page.getByTestId('intro-resume');
  const resumeBox = await resume.boundingBox();
  expect(brand?.y ?? 9999).toBeLessThan(812);
  expect((sentence?.y ?? 9999) + (sentence?.height ?? 0)).toBeLessThanOrEqual(812);
  expect((resumeBox?.y ?? 9999) + (resumeBox?.height ?? 0)).toBeLessThanOrEqual(812);
  await expect(resume).toHaveAttribute('href', '/api/docs/resume');
  // Everything pressable in the strip, and the hamburger over it, is a full 44 pixel target.
  for (const target of [
    resume,
    page.getByTestId('intro-author'),
    page.getByTestId('intro-built'),
    page.getByTestId('intro-admin'),
    page.getByTestId('intro-dismiss'),
    page.getByRole('button', { name: 'Menu' }),
  ]) {
    const box = await target.boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }
  // It never covers the inventory: the title is under it, not behind it.
  const title = await page.getByRole('heading', { level: 1, name: 'Inventory' }).boundingBox();
  const stripBox = await strip.boundingBox();
  expect(title?.y ?? 0).toBeGreaterThanOrEqual((stripBox?.y ?? 0) + (stripBox?.height ?? 0));
  // Dismissed, it stays dismissed in this browser.
  await page.getByTestId('intro-dismiss').click();
  await expect(strip).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Inventory' })).toBeVisible({
    timeout: 20_000,
  });
  await expect(page.getByTestId('intro-strip')).toHaveCount(0);
});
