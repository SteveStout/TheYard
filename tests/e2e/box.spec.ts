import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region box-live
/**
 * The box's card (ADR: The box reports itself, the addendum on the live panels): it opens from its address in
 * the one frame with its four live panels above the chart; where no agent posts, as on the gate's runner, the
 * panels say so in blue; the panels ask for the ring every two seconds while the card is open, and not at all
 * while the tab is hidden.
 */
test('the box card draws its four live panels and polls the ring only while the tab is in view', async ({
  page,
}) => {
  const asked: number[] = [];
  page.on('request', (request) => {
    if (request.url().includes('/api/admin/cloud/box/live')) asked.push(Date.now());
  });
  await openTheYard(page, '/?view=admin&card=box');
  await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', 'box');
  const card = page.getByTestId('box-card');
  await expect(card.getByTestId('cloud-dial-left')).toBeVisible({ timeout: 30_000 });
  for (const key of ['cores', 'memory', 'network', 'gpus']) {
    await expect(card.getByTestId(`box-live-${key}`)).toBeVisible();
    await expect(card.getByTestId(`box-live-${key}`).getByRole('img')).toHaveAttribute(
      'aria-label',
      /the last 60 seconds/
    );
  }
  await expect(card.getByTestId('box-live-stale')).toContainText('no live sample');
  await expect(card.getByTestId('cloud-rows').locator('[data-tone]')).toHaveCount(8);

  // Visible: about three reads in six seconds, at one every two.
  const from = asked.length;
  await page.waitForTimeout(6_000);
  const visible = asked.length - from;
  expect(visible, 'reads in six seconds with the tab in view').toBeGreaterThanOrEqual(2);
  expect(visible, 'reads in six seconds with the tab in view').toBeLessThanOrEqual(4);

  // Hidden: none.
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  const hiddenFrom = asked.length;
  await page.waitForTimeout(5_000);
  expect(asked.length - hiddenFrom, 'reads while the tab is hidden').toBe(0);
});
// #endregion box-live
