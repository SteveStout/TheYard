import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { CLOUD_RESOURCES } from '../../src/lib/workbench';
import { PROCESS_STRIP_CELLS } from '../../src/lib/cloudProcesses';

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
  // One test walks every card, so its time grows with the list: fifteen seconds a card, not a fixed
  // minute that the eleventh card ran past on the laptop (1.0.4.52's first local check).
  test.setTimeout(15_000 * CLOUD_RESOURCES.length);
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

// #region nightly-processes
/**
 * The nightly card's six processes (ADR-109, the addendum on the six processes): the description and six
 * sections arrive with every run history closed and no runs asked for; opening one asks the runs route once
 * and shows its runs with a trace link (or, where the workspace cannot be read, the sentence that says why);
 * the pills change the window; and on a phone the sections are one column and the runs stack, so neither the
 * table nor the page scrolls sideways (the operator's look: an Admin table fits or stacks). The strip's cell
 * count is the one constant the page draws from.
 */
test('the nightly card draws six processes whose histories open on request', async ({ page }) => {
  test.setTimeout(90_000);
  const asked: string[] = [];
  page.on('request', (request) => {
    if (request.url().includes('/api/admin/cloud/nightly/runs/')) asked.push(request.url());
  });
  await openTheYard(page, '/?view=admin&card=nightly');
  const card = page.getByTestId('nightly-card');
  await expect(card.getByTestId('cloud-processes')).toBeVisible({ timeout: 30_000 });
  await expect(card.getByTestId('cloud-description')).toContainText('What it is.');
  const sections = card.locator('[data-testid^="process-"][data-tone]');
  await expect(sections).toHaveCount(6);
  for (const key of ['AtsSweep', 'Merger', 'Nightly', 'SheetMirror', 'DailyCard', 'MergerHealth']) {
    await expect(card.getByTestId(`process-history-${key}`)).not.toHaveAttribute('open', /.*/);
    await expect(card.getByTestId(`process-strip-${key}`).locator('rect')).toHaveCount(
      PROCESS_STRIP_CELLS
    );
  }
  expect(asked).toHaveLength(0);

  const history = card.getByTestId('process-history-AtsSweep');
  await history.locator('summary').click();
  await expect(card.getByTestId('process-totals-AtsSweep')).toBeVisible({ timeout: 30_000 });
  expect(asked).toHaveLength(1);
  expect(asked[0]).toContain('/runs/AtsSweep?window=7d&page=1');
  const runs = history.getByTestId('process-run');
  if ((await runs.count()) > 0) {
    await expect(runs.first().getByTestId('process-trace')).toHaveAttribute(
      'href',
      /portal\.azure\.com/
    );
  } else {
    await expect(history.getByTestId('process-note-AtsSweep')).not.toBeEmpty();
  }
  const weekCount = await card.getByTestId('process-count-AtsSweep').textContent();
  await card.getByTestId('process-window-AtsSweep-24h').click();
  await expect(card.getByTestId('process-count-AtsSweep')).toContainText('24 hours', {
    timeout: 30_000,
  });
  expect(weekCount).toContain('7 days');
  expect(asked).toHaveLength(2);
});

test('on a phone each process is one column and its runs stack, so nothing scrolls sideways', async ({
  page,
}) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page, '/?view=admin&card=nightly');
  const card = page.getByTestId('nightly-card');
  const section = card.getByTestId('process-AtsSweep');
  await expect(section).toBeVisible({ timeout: 30_000 });
  const columns = await section.evaluate(
    (node) => getComputedStyle(node).gridTemplateColumns.split(' ').length
  );
  expect(columns).toBe(1);
  await card.getByTestId('process-history-AtsSweep').locator('summary').click();
  await expect(card.getByTestId('process-totals-AtsSweep')).toBeVisible({ timeout: 30_000 });
  const table = card.getByTestId('process-table-AtsSweep');
  if ((await table.count()) > 0) {
    const sideways = await table.evaluate((node) => node.scrollWidth > node.clientWidth + 1);
    expect(sideways).toBe(false);
  }
  const pageScrolls = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth + 1
  );
  expect(pageScrolls).toBe(false);
});
// #endregion nightly-processes
