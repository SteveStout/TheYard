import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region quality
/**
 * The Quality page (ADR-130, Quality you can watch, and its addendum on the page in two layers): at a desk's width and
 * a phone's, the window's bar is the one title; the reading layer is the lede with its four readouts, the slip and six
 * promises with no gauge; the engineer layer is seven folds, every one closed on load, and each opens to its block:
 * the fifteen tiles under the held pill, every trend fence a drawn chart with the version its number joined marked in
 * gold (the first chart twice, its wide and its phone shape, one shown), the coverage charts with their floors, the
 * rulebook's six cards with the full table folded inside, CodeRabbit's four readouts over its folded log. No live
 * number is left as a note, and nothing scrolls sideways but the tables' own scrollers. The number of charts is read
 * from the document the API serves, never typed.
 */
for (const width of [1440, 390]) {
  test(`at ${width} the Quality page is its picture: five cards, four readouts, six promises, seven closed folds`, async ({
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
    // Five cards in the picture's order, the three numbered ones keeping the shared q-card hook.
    const cards = dialog.locator(
      '[data-mock="lede-card"], [data-card="slip"], [data-card="promise"], [data-card="engineer"], [data-mock="close"]'
    );
    await expect(cards).toHaveCount(5);
    expect(
      await cards.evaluateAll((all) =>
        all.map((e) => e.getAttribute('data-card') ?? e.getAttribute('data-mock'))
      )
    ).toEqual(['lede-card', 'slip', 'promise', 'engineer', 'close']);
    await expect(dialog.locator('[data-mock="q-card"][data-card]')).toHaveCount(3);
    // Four readouts with values, one test number among them; six promises, no gauge, no count.
    await expect(dialog.locator('[data-mock="readout-label"]')).toHaveText([
      'quality numbers held',
      'tests passed',
      'of lines covered',
      'versions AI-reviewed',
    ]);
    for (const value of await dialog.locator('[data-mock="readout-value"]').allTextContents())
      expect(value).toMatch(/^[\d,]+%?( of [\d,]+)?$/);
    await expect(dialog.locator('[data-mock="promise"]')).toHaveCount(6);
    await expect(
      dialog.locator('[data-card="promise"] svg, [data-card="promise"] .quality-group')
    ).toHaveCount(0);
    await expect(dialog.locator('[data-mock="promise-foot"]')).toHaveText(
      /^[\d,]+ rules in all, each written down with its reason and held by a test on every release\.$/
    );
    // Seven folds, every one closed on load; the engineer layer is one row each until it is opened.
    const folds = dialog.locator('[data-mock="fold"]');
    await expect(folds).toHaveCount(7);
    await expect(dialog.locator('[data-mock="fold"][open]')).toHaveCount(0);
    await expect(dialog.locator('.quality-tile').first()).toBeHidden();
    // Each fold opens to its block.
    const blocks: Record<string, string> = {
      numbers: '.quality-tile',
      history: '.quality-trend[data-form="small"]',
      coverage: '[data-testid="quality-chart-floor"]',
      rulebook: '.quality-group',
      coderabbit: '.quality-readout',
      thin: 'li',
      source: 'a[href*="quality-history.json"]',
    };
    for (const [key, block] of Object.entries(blocks)) {
      const fold = dialog.locator(`[data-mock="fold"][data-key="${key}"]`);
      await fold.locator('> summary').click();
      await expect(fold).toHaveAttribute('open', '');
      await expect(fold.locator(block).first()).toBeVisible();
    }
    await expect(dialog.locator('.quality-readout')).toHaveCount(4);
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
    await expect(
      dialog.locator('summary', { hasText: /^Every version's review \(/ })
    ).toBeVisible();
    await expect(
      dialog.locator('[data-mock="fold-title"]', { hasText: "CodeRabbit's log" })
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
// The Quality sheet takes the sales look (saleshome1120, ADR-130's addendum on the panel, the numbers and the foot; the
// quality1123 picture for the lede): the lede's readouts are four dark cells with no head and no LIVE tag, every titled
// card carries its number, and the sheet ends on the review route's closing question with its three moves and Where
// next, opened over the review route as the picture draws it.
for (const width of [1440, 390]) {
  test(`at ${width} the Quality sheet has the dark readouts, its numbered cards and its foot`, async ({
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
    const readouts = dialog.locator('[data-mock="lede-card"] [data-mock="readout"]');
    await expect(readouts).toHaveCount(4, { timeout: 30_000 });
    await expect(dialog.locator('.op-live-head')).toHaveCount(0);
    expect(await readouts.first().evaluate((e) => getComputedStyle(e).backgroundImage)).toContain(
      'linear-gradient'
    );
    // Four across at a desk, two by two on a phone.
    const rows = await readouts.evaluateAll((all) =>
      all.map((e) => Math.round(e.getBoundingClientRect().top))
    );
    expect(new Set(rows).size).toBe(width < 640 ? 2 : 1);
    await expect(dialog.getByTestId('doc-note')).toHaveCount(0);
    // CodeRabbit's readouts stay a row of light tiles, in their fold.
    await dialog.locator('[data-mock="fold"][data-key="coderabbit"] > summary').click();
    const tile = dialog.locator('.quality-readouts .quality-readout').first();
    await expect(tile).toBeVisible();
    expect(await tile.evaluate((e) => getComputedStyle(e).backgroundImage)).toBe('none');
    await expect(dialog.locator('[data-mock="q-card"] [data-mock="section-num"]')).toHaveText([
      '01',
      '02',
      '03',
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
// One test number on the site: the lede's tests readout is the landing's own figure on the same build, summed over
// the same suites (QualityLedeTests holds the list), so a reader who follows the landing's tile here meets its number.
test('the Quality lede counts the tests the landing counts', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openTheYard(page, '/?doc=quality');
  const readout = page.locator('dialog[open] [data-mock="readout"][data-key="tests"]');
  await expect(readout.locator('[data-mock="readout-label"]')).toHaveText('tests passed', {
    timeout: 30_000,
  });
  const figure = (await readout.locator('[data-mock="readout-value"]').textContent()) ?? '';
  expect(figure).toMatch(/^\d[\d,]*$/);
  // The landing counts up to its figure, so its tile is read once it has arrived at it.
  await openTheYard(page, '/');
  await expect(page.getByTestId('landing-proof-tests')).toContainText(
    new RegExp(`^${figure} *tests`),
    { timeout: 30_000 }
  );
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
  await expect(dialog.locator('[data-mock="readout"]').first()).not.toBeInViewport();
});
