import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

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

// #region cloud-resources
/**
 * The six resource cards after the plan (ADR: One frame for every resource): each opens from its
 * address in the same frame as the plan, with its own key in the rail, two dials, four questions
 * and a readout, and a local run with nothing kept says so in a sentence.
 */
test('every resource under Cloud infrastructure draws in the one frame', async ({ page }) => {
  for (const slug of ['sqldb', 'cosmosdb', 'registry', 'insights', 'mail', 'edge']) {
    await openTheYard(page, `/?view=admin&card=${slug}`);
    await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', slug);
    await expect(page.getByTestId('bench-crumb')).toContainText('Cloud infrastructure');
    const card = page.getByTestId(`${slug}-card`);
    await expect(card.getByTestId('cloud-dial-left')).toBeVisible({ timeout: 30_000 });
    await expect(card.getByTestId('cloud-dial-right')).toBeVisible();
    await expect(card.getByTestId('cloud-rows').locator('[data-tone]')).toHaveCount(4);
    await expect(card.getByTestId('cloud-readout')).toBeVisible();
    await expect(card.getByTestId('cloud-note')).toBeVisible();
  }
});
// #endregion cloud-resources
