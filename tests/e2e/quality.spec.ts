import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region quality
/**
 * The Quality page (ADR-130, Quality you can watch, and its addendum on the page as its picture): at a desk's width and
 * a phone's, the window's bar is the one title, the lede carries the four readouts, the fifteen numbers are fifteen
 * tiles under the held pill, every trend fence becomes a drawn chart with the version its number joined marked in
 * gold (the first chart twice, its wide and its phone shape, one shown), the coverage charts carry their floors, the
 * rulebook is six cards with the full table folded inside the card, CodeRabbit's four readouts sit over its folded
 * log, no live number is left as a note, and nothing scrolls sideways but the tables' own scrollers. The number of
 * charts is read from the document the API serves, never typed.
 */
for (const width of [1440, 390]) {
  test(`at ${width} the Quality page is its picture: one title, readouts, fifteen tiles, every chart drawn`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    const served = await (await page.request.get('/api/docs/quality')).text();
    const fences = (served.match(/^```quality-trend/gm) ?? []).length;
    const floors = (served.match(/^[^\n|]+\| floor \d+ \|/gm) ?? []).length;
    expect(fences).toBeGreaterThan(8);
    expect(floors).toBe(2);
    expect(served).not.toContain('(no live measure');
    expect(served).not.toContain('is not in this build');

    await openTheYard(page, '/?doc=quality');
    const dialog = page.locator('dialog[open]');
    await expect(dialog.locator('h2').first()).toHaveText('Quality you can watch', {
      timeout: 30_000,
    });
    // One title: the bar's. The document's first heading is kept for a screen reader and not drawn again.
    const title = await dialog.locator('h1').boundingBox();
    expect(title!.width).toBeLessThanOrEqual(1);
    await expect(dialog.locator('.quality-readout')).toHaveCount(8);
    await expect(dialog.locator('.quality-tile')).toHaveCount(15);
    await expect(dialog.locator('.quality-held')).toContainText(
      /^\d+ of 15 at or under their limit on /
    );
    await expect(dialog.locator('.quality-group')).toHaveCount(6);
    // The slip draws at two shapes, one shown; every other fence once. Each number's chart marks its join.
    await expect(dialog.getByTestId('quality-chart')).toHaveCount(fences + 1, { timeout: 30_000 });
    await expect(dialog.getByTestId('quality-chart-join')).toHaveCount(fences + 1 - floors);
    await expect(dialog.getByTestId('quality-chart-floor')).toHaveCount(floors);
    await expect(dialog.getByTestId('quality-chart-callout').filter({ visible: true })).toHaveCount(
      3
    );
    await expect(dialog.locator('summary', { hasText: 'The full rulebook' })).toBeVisible();
    await expect(dialog.locator('summary', { hasText: "Every version's review" })).toBeVisible();
    await expect(
      dialog.getByRole('heading', { name: 'CodeRabbit, the last reviewer' })
    ).toBeVisible();
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways).toBeLessThanOrEqual(0);
    // Three tiles a row at a desk, two on a phone; the page behind is a soft field from 640 up.
    const tops = await dialog
      .locator('.quality-tile')
      .evaluateAll((all) =>
        all.slice(0, 3).map((tile) => Math.round(tile.getBoundingClientRect().top))
      );
    expect(new Set(tops).size).toBe(width < 640 ? 2 : 1);
    if (width >= 640) {
      const sheet = await dialog.evaluate((d) => ({
        width: d.getBoundingClientRect().width,
        behind: getComputedStyle(d, '::backdrop').backdropFilter,
      }));
      expect(Math.abs(sheet.width - 1080)).toBeLessThanOrEqual(1);
      expect(sheet.behind).toContain('blur(22px)');
    }
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
    await expect(dialog.getByTestId('quality-chart').first()).toBeAttached({ timeout: 30_000 });
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
// The section was merged into "The slip, and the ratchet" (the quality1120 picture); the old address keeps landing there.
test('a link to the slip and the turn opens the Quality page on that section', async ({ page }) => {
  await page.setViewportSize({ width: 1568, height: 772 });
  await openTheYard(page, '/?doc=quality#the-slip-and-the-turn');
  const dialog = page.locator('dialog[open]');
  const slip = dialog.locator('h2[id="the-slip-and-the-turn"]');
  await expect(slip).toHaveText('The slip, and the ratchet', { timeout: 30_000 });
  await expect(slip).toBeInViewport();
  await expect(dialog.locator('.quality-readout').first()).not.toBeInViewport();
});
