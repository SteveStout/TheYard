import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region quality
/**
 * The Quality page (ADR-130, Quality you can watch): at a desk's width and a phone's, every trend fence becomes a
 * drawn line with 1.0.4.115 marked, the rules table and CodeRabbit's log are there, no live number is left as a
 * note, and nothing scrolls sideways but the tables' own scrollers. The number of charts is read from the document
 * the API serves, never typed.
 */
for (const width of [1280, 390]) {
  test(`at ${width} the Quality page draws every number's line from the history and marks the ratchet`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    const served = await (await page.request.get('/api/docs/quality')).text();
    const fences = (served.match(/^```quality-trend/gm) ?? []).length;
    expect(fences).toBeGreaterThan(8);
    expect(served).not.toContain('(no live measure');
    expect(served).not.toContain('is not in this build');

    await openTheYard(page, '/?doc=quality');
    const dialog = page.locator('dialog[open]');
    await expect(dialog.locator('h2').first()).toHaveText('Quality you can watch', {
      timeout: 30_000,
    });
    await expect(dialog.getByTestId('quality-trend')).toHaveCount(fences, { timeout: 30_000 });
    // Every number's chart marks the version it joined the ratchet; the two coverage charts have no such version.
    await expect(dialog.getByTestId('quality-trend-marker')).toHaveCount(fences - 2);
    await expect(
      dialog.getByRole('heading', { name: 'The rules, and the tests that hold them' })
    ).toBeVisible();
    await expect(
      dialog.getByRole('heading', { name: 'CodeRabbit, the last reviewer' })
    ).toBeVisible();
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways).toBeLessThanOrEqual(0);
  });

  test(`at ${width} a chart's place on the Quality page is its height before the chart is drawn into it`, async ({
    page,
  }) => {
    // The charts mount into their placeholders after the words have painted (useLiveBlocks.tsx). An empty one was
    // 0 px, so on a slow phone every word under the first chart dropped 200 px when it arrived (0.123 at 390 under
    // 4x CPU throttling, 15 November). An empty placeholder beside a drawn chart is the drawn chart's height.
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await openTheYard(page, '/?doc=quality');
    const dialog = page.locator('dialog[open]');
    await expect(dialog.getByTestId('quality-trend').first()).toBeVisible({ timeout: 30_000 });
    const [drawn, empty] = await page.evaluate(() => {
      const box = document.querySelector('dialog[open] [data-quality-trend]') as HTMLElement;
      const spare = document.createElement('div');
      spare.setAttribute('data-quality-trend', '{}');
      box.after(spare);
      return [box.getBoundingClientRect().height, spare.getBoundingClientRect().height];
    });
    expect(drawn).toBeGreaterThanOrEqual(150);
    expect(Math.abs(empty - drawn)).toBeLessThanOrEqual(1);
  });
}
// #endregion quality

// The review route's "See the proof in 2 minutes" opens Quality at one section, so the address it carries has to
// land there on a cold load, the way a link from outside the site arrives: the heading's id comes from its words
// (markdown.ts) and the section is scrolled to once the document is drawn (useLiveBlocks.tsx). The first chart,
// near the top of the page, is off the screen; the dialog's own title stays in view, so it says nothing here.
test('a link to the slip and the turn opens the Quality page on that section', async ({ page }) => {
  await page.setViewportSize({ width: 1568, height: 772 });
  await openTheYard(page, '/?doc=quality#the-slip-and-the-turn');
  const dialog = page.locator('dialog[open]');
  const slip = dialog.locator('h2[id="the-slip-and-the-turn"]');
  await expect(slip).toHaveText('The slip, and the turn', { timeout: 30_000 });
  await expect(slip).toBeInViewport();
  await expect(dialog.getByTestId('quality-trend').first()).not.toBeInViewport();
});
