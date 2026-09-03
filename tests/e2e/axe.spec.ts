import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { signIn } from './signIn';

/**
 * The accessibility rules a machine can check (ADR: The accessibility check).
 *
 * This is not an audit. Automated tooling catches a minority of WCAG failures,
 * and the ones it cannot see are the ones about meaning: whether a label says
 * something useful, whether an order makes sense to somebody who cannot see the
 * layout. What it is good at is exactly what a person is bad at, which is
 * checking every element on every view on every run.
 *
 * It is here because it found two real contrast failures on its first run, on
 * the two most visible elements on the page, in a repository that already had a
 * passing contrast test.
 */
// #region axe
// Standard rules only. Best-practice rules outside WCAG are deliberately left
// out: a check that fails on advice rather than on a standard is a check people
// start ignoring, and an ignored check is worse than none.
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

async function violations(page: Page): Promise<string[]> {
  const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
  return results.violations.map(
    (v) =>
      `${v.id} (${v.impact}) on ${v.nodes.length} node(s), first: ${v.nodes[0]?.target.join(' ')} :: ` +
      `${v.nodes[0]?.failureSummary?.replace(/\n/g, ' | ')}`
  );
}

test.describe('WCAG 2.1 AA, on every view', () => {
  test('the inventory', async ({ page }) => {
    await page.goto('/');
    // The heading is there before the fetch returns. Waiting for a tile is
    // waiting for the hundred cards, their badges and their countdowns, which
    // is where both of the contrast failures were: a scan that ran on the
    // heading alone could have found nothing and reported a clean page (the
    // staff review, 2026-09-03).
    await expect(page.locator('article h3 button').first()).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });
  // #endregion axe

  test('a vehicle', async ({ page }) => {
    await page.goto('/');
    await page.locator('article h3 button').first().click();
    await expect(page.getByRole('button', { name: 'Back to inventory' })).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test('the admin tab', async ({ page }) => {
    await page.goto('/?view=admin');
    await expect(page.getByRole('heading', { name: 'Admin' })).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test('a document, open', async ({ page }) => {
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Project documents' });
    await nav.getByRole('button', { name: 'Project README' }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test('the inventory on a phone', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/');
    await expect(page.locator('article h3 button').first()).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test('the records index, open', async ({ page }) => {
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Project documents' });
    // Closed, the index's rows are not in the accessibility tree at all, so
    // every other test in this file was scanning a rail with a third of its
    // controls hidden.
    await nav.getByText('Decision Records', { exact: true }).click();
    await expect(nav.getByRole('button', { name: 'ADR: Front Door origin' })).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test('the account view, signed out', async ({ page }) => {
    await page.goto('/?view=account');
    await expect(page.getByRole('heading', { name: 'Sign in to bid' })).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test('the account view, signed in', async ({ page }) => {
    // A different page: an identity block, a sign-out, and a bid list rather
    // than a form. Scanning only the signed-out half would leave the half with
    // the coloured winning and outbid states unchecked.
    const email = await signIn(page);
    await page.goto('/?view=account');
    await expect(page.getByRole('heading', { name: email })).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test('the phone drawer, open', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/');
    await page.getByRole('button', { name: 'Menu' }).click();
    await expect(page.getByRole('dialog', { name: 'Menu' })).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });
});
