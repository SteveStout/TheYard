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
// The Quality sheet takes the sales look (saleshome1120, ADR-130's addendum on the panel, the numbers and the foot): the
// readouts are the dark live panel, every titled card carries its number, and the sheet ends on the review route's
// closing question with its three moves and Where next, opened over the review route as the picture draws it.
for (const width of [1440, 390]) {
  test(`at ${width} the Quality sheet has the dark panel, its numbered cards and its foot`, async ({
    page,
  }) => {
    const reported: string[] = [];
    await page.route('**/api/review/reached', async (route) => {
      reported.push(String((route.request().postDataJSON() as { action?: string }).action));
      await route.fulfill({ status: 204 });
    });
    await page.route('**/api/go/book', (route) => route.fulfill({ status: 204 }));
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await openTheYard(page, '/?view=review&doc=quality');
    const dialog = page.locator('dialog[open]');
    const panel = dialog.locator('[data-mock="live-panel"]');
    await expect(panel).toHaveCount(1, { timeout: 30_000 });
    await expect(panel.locator('.op-live-head')).toHaveText(/The ratchet, right now\s*Live/);
    await expect(panel.locator('[data-mock="live-value"]')).toHaveCount(4);
    expect(await panel.evaluate((e) => getComputedStyle(e).backgroundImage)).toContain(
      'linear-gradient'
    );
    await expect(dialog.getByTestId('doc-note')).toHaveCount(0);
    // CodeRabbit's readouts stay a row of tiles.
    await expect(
      dialog.locator('.quality-readouts:not(.op-live-panel) .quality-readout')
    ).toHaveCount(4);
    await expect(dialog.locator('[data-mock="q-card"] [data-mock="section-num"]')).toHaveText([
      '01',
      '02',
      '03',
      '04',
      '05',
      '06',
      '07',
      '08',
      '09',
      '10',
    ]);
    const foot = dialog.locator('[data-mock="q-card"] ~ [data-mock="close"]');
    await expect(foot).toHaveCount(1);
    await expect(foot.locator('[data-mock="close-question"]')).toHaveText(
      'What has to work, where is it today, and what does success look like?'
    );
    const moves = foot.locator('[data-mock="close-moves"] > a');
    await expect(moves).toHaveText([
      'Book 20 minutes',
      /Resume \(PDF\)$/,
      'Message me on LinkedIn',
    ]);
    await expect(moves.nth(1)).toHaveClass(/op-pill-resume/);
    await expect(foot.locator('[data-mock="next-link"]')).toHaveText([
      'Back to the three questions',
      'Quality on both ends',
    ]);
    // The question beside its moves at a desk; the moves under it, each the row's width, on a phone.
    const ask = foot.locator('[data-mock="close-ask"]');
    const [question, row] = await ask.evaluate((e) =>
      [...e.children].map((c) => {
        const box = c.getBoundingClientRect();
        return { left: box.left, top: box.top, bottom: box.bottom, width: box.width };
      })
    );
    if (width === 390) expect(row.top).toBeGreaterThanOrEqual(question.bottom - 1);
    else expect(row.left).toBeGreaterThan(question.left + question.width - 1);
    for (const move of await moves.all()) {
      const box = (await move.boundingBox())!;
      expect(box.height, 'a move is a thumb tall').toBeGreaterThanOrEqual(44);
    }
    // Each move reports the action its twin on the review route reports.
    await moves.nth(0).click();
    await expect.poll(() => reported).toContain('book');
  });
}

test('opened over the review route, Back to the three questions closes the sheet and lands on section 02', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openTheYard(page, '/?view=review&doc=quality');
  const dialog = page.locator('dialog[open]');
  const back = dialog.getByRole('link', { name: 'Back to the three questions' });
  await expect(back).toHaveAttribute('href', /^\/\?view=review(&r=[a-z0-9]{6})?#questions$/, {
    timeout: 30_000,
  });
  await back.click();
  await expect(page.locator('dialog[open]')).toHaveCount(0);
  await expect(page).not.toHaveURL(/doc=quality/);
  await expect(page.locator('#questions')).toBeInViewport();
});
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
  await expect(slip).toHaveText(/^01\s*The slip, and the ratchet$/, { timeout: 30_000 });
  await expect(slip).toBeInViewport();
  await expect(dialog.locator('.quality-readout').first()).not.toBeInViewport();
});
