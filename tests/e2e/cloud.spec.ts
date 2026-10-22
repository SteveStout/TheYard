import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { CLOUD_RESOURCES } from '../../src/lib/workbench';

// #region cloud-plan
/**
 * The App Service plan card under Cloud infrastructure (ADR: The App Service plan, read every
 * minute): the section sits after the cost question with the plan first, the card draws both
 * dials and the four questions in the shared frame, and a run with no Azure identity says so in
 * a sentence rather than drawing a zero.
 */
test('the plan card opens Cloud infrastructure and draws its dials and questions', async ({
  page,
}) => {
  await openTheYard(page, '/?view=admin&card=plan');
  await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', 'plan');
  await expect(page.getByTestId('bench-crumb')).toContainText('Cloud infrastructure');
  const card = page.getByTestId('plan-card');
  await expect(card.getByTestId('cloud-dial-left')).toBeVisible({ timeout: 30_000 });
  await expect(card.getByTestId('cloud-dial-right')).toBeVisible();
  await expect(card.getByTestId('cloud-rows').locator('[data-tone]')).toHaveCount(4);
  await expect(card.getByTestId('cloud-readout')).toBeVisible();
  // A local run has no identity to ask Azure with: the dials say "not read", never 0 %.
  await expect(card.getByTestId('cloud-dial-left')).not.toContainText('0 %');
  const rail = page.getByTestId('bench-rail');
  const links = rail.locator('[data-testid^="bench-link-"]');
  const slugs = await links.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-testid'))
  );
  expect(slugs.indexOf('bench-link-plan')).toBeLessThan(slugs.indexOf('bench-link-azure'));
  expect(slugs.indexOf('bench-link-azure')).toBeLessThan(slugs.indexOf('bench-link-machines'));
});
// #endregion cloud-plan

// #region card-shell
/**
 * The CardShell's slots (ADR: The onion cut into vertical slices, the CardShell): the tone chip
 * beside the title, the spec's source, the thresholds as chips, the rail's tone dot read before
 * the card is opened, and up and down walking the rail.
 */
test('the shell draws the tone, the chips and the rail dot, and the arrows walk the rail', async ({
  page,
}) => {
  await openTheYard(page, '/?view=admin&card=plan');
  const card = page.getByTestId('plan-card');
  await expect(card.getByTestId('card-tone')).toBeVisible({ timeout: 30_000 });
  await expect(card.getByTestId('card-thresholds').locator('li').first()).toBeVisible();
  const rail = page.getByTestId('bench-rail');
  await expect(rail.getByTestId('bench-tone-sqldb')).toBeVisible({ timeout: 30_000 });
  // Every card under Cloud infrastructure calculates a status, the three that draw their own body too.
  for (const slug of ['azure', 'machines', 'follows']) {
    await expect(rail.getByTestId(`bench-tone-${slug}`)).toBeVisible({ timeout: 30_000 });
  }
  await rail.getByTestId('bench-link-plan').focus();
  await page.keyboard.press('ArrowDown');
  await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', 'sqldb');
  await expect(rail.getByTestId('bench-link-sqldb')).toBeFocused();
  await page.keyboard.press('ArrowUp');
  await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', 'plan');
});
// #endregion card-shell

// #region cloud-resources
/**
 * The resource cards after the plan (ADR: One frame for every resource): each opens from its
 * address in the same frame as the plan, with its own key in the rail, two dials, four questions
 * (five on the nightly card) and a readout, and a local run with nothing kept says so in a sentence.
 */
test('every resource under Cloud infrastructure draws in the one frame', async ({ page }) => {
  for (const slug of CLOUD_RESOURCES) {
    await openTheYard(page, `/?view=admin&card=${slug}`);
    await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', slug);
    await expect(page.getByTestId('bench-crumb')).toContainText('Cloud infrastructure');
    const card = page.getByTestId(`${slug}-card`);
    await expect(card.getByTestId('cloud-dial-left')).toBeVisible({ timeout: 30_000 });
    await expect(card.getByTestId('cloud-dial-right')).toBeVisible();
    // Four questions on every card, and a fifth on the nightly card: whether the tracker merger is
    // keeping up (ADR: The tracker merger runs in Azure).
    await expect(card.getByTestId('cloud-rows').locator('[data-tone]')).toHaveCount(
      slug === 'nightly' ? 5 : 4
    );
    await expect(card.getByTestId('cloud-readout')).toBeVisible();
    await expect(card.getByTestId('cloud-note')).toBeVisible();
  }
});
// #endregion cloud-resources
