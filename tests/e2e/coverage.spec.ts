import { expect, test, type Browser, type APIRequestContext, type Page } from '@playwright/test';
import { readPage, siteList, type SitePage } from './coverage';

// #region painted
/**
 * The page has what the reader measures: every face is loaded, so a width is the page's own and
 * not a fallback face's; every picture in view has arrived or failed, so a box has its final
 * size (a lazy picture below the fold is not asked for, because the browser will not fetch it
 * until it scrolls into view and the wait would never end); and the browser has painted two
 * frames since, so a layout that follows the last change is in. A named condition in place of a
 * timer: a timer is a guess at how long the page needs, and the guess was three hundred
 * milliseconds on about three hundred and eighty pages a run.
 */
async function painted(tab: Page): Promise<void> {
  await tab.evaluate(async () => {
    await document.fonts.ready;
    const inView = (image: HTMLImageElement) => {
      const box = image.getBoundingClientRect();
      return box.bottom > 0 && box.top < innerHeight && box.right > 0 && box.left < innerWidth;
    };
    const pictures = Promise.all(
      Array.from(document.images)
        .filter((image) => !image.complete && (image.loading !== 'lazy' || inView(image)))
        .map(
          (image) =>
            new Promise<void>((settled) => {
              image.addEventListener('load', () => settled(), { once: true });
              image.addEventListener('error', () => settled(), { once: true });
            })
        )
    );
    // A picture that fires neither load nor error (one whose source is swapped while it is
    // waited on, or one the browser decides not to fetch) must not hold the read for ever; after
    // five seconds the page is read as it stands, which is what a quiet network read too.
    await Promise.race([pictures, new Promise<void>((enough) => setTimeout(enough, 5000))]);
    await new Promise<void>((done) =>
      requestAnimationFrame(() => requestAnimationFrame(() => done()))
    );
  });
}
// #endregion painted

/**
 * Every page the same, none missed (ADR: The glass look, the addendum on every
 * page the same). One test per width: every page the site lists for itself is
 * opened and read in the browser, and a page passes when its body is IBM Plex
 * Sans, it draws at least one panel, every panel carries the brackets and the
 * 3 px rule, no button is square, no word is set in another face (code
 * excepted), no id is used twice and no drawing's reference is lost. StyleRulesTests rules nine and thirteen are the static half: a sheet that forgot
 * the look fails there, and a page that rendered without it fails here.
 */
async function everyPage(
  browser: Browser,
  request: APIRequestContext,
  width: number
): Promise<void> {
  const pages = await siteList(request);
  expect(pages.length, 'the site lists fewer pages than its views and cards').toBeGreaterThan(30);
  const phone = width < 600;
  const context = await browser.newContext({
    baseURL: test.info().project.use.baseURL,
    viewport: { width, height: phone ? 844 : 900 },
    isMobile: phone,
    hasTouch: phone,
    reducedMotion: 'reduce',
  });
  // The ribbon ground is hidden, as every spec here hides it (app.ts, ribbons-off).
  await context.addInitScript(() => {
    addEventListener('DOMContentLoaded', () => {
      const style = document.createElement('style');
      style.textContent = '[data-testid^="ribbons"] { display: none !important; }';
      document.head.append(style);
    });
  });
  const queue: SitePage[] = [...pages];
  const failures: string[] = [];
  // Three tabs at a time: the pages are independent, one at a time took minutes a width, and
  // more than three at once, beside the gate's other suite, starved the listing's first answer.
  await Promise.all(
    Array.from({ length: 3 }, async () => {
      const tab = await context.newPage();
      for (let next = queue.shift(); next; next = queue.shift()) {
        // The load event is the browser's own word that the document, its sheets and its images are
        // in; what the page draws after that is read off the page's own signals below, not off a
        // quiet network. Waiting for the network to go quiet cost half a second a page at best and
        // more on a page that polls, and this read is about three hundred and eighty pages a run.
        // A page that has not loaded in a minute fails the read by name instead of holding it.
        await tab.goto(next.address, { waitUntil: 'load', timeout: 60_000 });
        // An app view has settled when its announcement names it; a page of its own has drawn
        // itself when its script has run (the API reference renders after its script loads).
        // /about is a page the API renders (AboutPage.cs), not a view of the app, so it is read
        // the way the API reference is: it has drawn when its panel is on the page.
        const served = next.address.startsWith('/api/') || next.address === '/about';
        if (!served) {
          await expect(tab.getByTestId('view-announcement')).not.toHaveText(/Loading/, {
            timeout: 60_000,
          });
          // An Admin card is a chunk of its own: the tab has drawn when the workbench is on the
          // page and no card is still on its way (a phone once read the log card's address before
          // either).
          if (next.address.includes('view=admin')) {
            await expect(tab.getByTestId('workbench')).toBeVisible({ timeout: 30_000 });
            await expect(tab.getByTestId('bench-loading')).toHaveCount(0, { timeout: 30_000 });
          }
          // A view that is still reading says so with a loading line of its own: the inventory,
          // which the vehicle page stands on, the Admin tab and the account. The read waits for
          // every such line to be gone, and on a vehicle page for the vehicle's own layout.
          await expect(
            tab.locator('main [class*="loadingInventory"], main [class*="adminLoading"]')
          ).toHaveCount(0, { timeout: 60_000 });
          if (next.address.includes('?vehicle=')) {
            await expect(tab.getByTestId('vehicle-layout')).toBeVisible({ timeout: 60_000 });
          }
          // A document is fetched after the view is announced, and its window shows its loading
          // line until the document is in; the read waits for that line to be gone.
          if (next.address.includes('?doc=')) {
            await expect(tab.locator('dialog [class*="docLoading"]')).toHaveCount(0, {
              timeout: 60_000,
            });
          }
        } else {
          // The API reference draws itself from a script of its own, which on a cold phone took
          // longer than a second and a half.
          await tab
            .waitForFunction(
              () => document.querySelector('.op-glass, .scalar-card') !== null,
              null,
              {
                timeout: 30_000,
              }
            )
            .catch(() => undefined);
        }
        await painted(tab);
        const facts = await tab.evaluate(readPage);
        const wrong = [
          facts.font.includes('IBM Plex Sans') ? '' : `the body is ${facts.font}`,
          facts.panels > 0 ? '' : 'no panel',
          facts.unstyled.length ? `unstyled ${facts.unstyled.join(', ')}` : '',
          facts.square.length ? `square ${facts.square.join(', ')}` : '',
          facts.wrongFace.length ? `another face ${facts.wrongFace.join(', ')}` : '',
          facts.twice.length ? `an id used twice ${facts.twice.join(', ')}` : '',
          facts.lost.length ? `a drawing's reference lost ${facts.lost.join(', ')}` : '',
          facts.anywhere.length ? `a cell breaks anywhere ${facts.anywhere.join(', ')}` : '',
          facts.sideways.length ? `a table scrolls sideways ${facts.sideways.join(', ')}` : '',
        ].filter(Boolean);
        if (wrong.length) failures.push(`${next.address} at ${width}: ${wrong.join('; ')}`);
      }
      await tab.close();
    })
  );
  await context.close();
  expect(failures, `${pages.length} pages read at ${width}`).toEqual([]);
}

test("every page the site lists is on the operator's look on a phone, 390 wide", async ({
  browser,
  request,
}) => {
  test.setTimeout(12 * 60_000);
  await everyPage(browser, request, 390);
});

test("every page the site lists is on the operator's look on a desk, 1280 wide", async ({
  browser,
  request,
}) => {
  test.setTimeout(12 * 60_000);
  await everyPage(browser, request, 1280);
});

// The narrowest desk: the rail docks at 1024 and the columns are at their tightest,
// which is where a table scrolling sideways would show first (the self-review of 26
// September; the record said 1024 or wider, and the gate read 1280 only).
test("every page the site lists is on the operator's look on the narrowest desk, 1024 wide", async ({
  browser,
  request,
}) => {
  test.setTimeout(12 * 60_000);
  await everyPage(browser, request, 1024);
});

// #region strip-rows
/**
 * The Admin strip and the landing stat row (the tweaks pass, A2): two across on
 * a phone and four on a desk, every tile in a row the same height, and every
 * number in a row on one baseline, read on the rendered page with the readings in.
 */
async function rowsOf(
  browser: Browser,
  width: number,
  address: string,
  tile: string,
  figure: string
): Promise<{ columns: number; rows: { tops: number[]; heights: number[] }[] }> {
  const phone = width < 600;
  const context = await browser.newContext({
    baseURL: test.info().project.use.baseURL,
    viewport: { width, height: phone ? 844 : 900 },
    isMobile: phone,
    hasTouch: phone,
    reducedMotion: 'reduce',
  });
  const tab = await context.newPage();
  await tab.goto(address);
  await expect(tab.locator(tile).first()).toBeVisible({ timeout: 60_000 });
  // Read once every tile has its reading: no tile waiting, every figure in words.
  await expect
    .poll(
      () =>
        tab.evaluate(
          ({ tile, figure }) =>
            Array.from(document.querySelectorAll(tile)).every(
              (element) =>
                element.getAttribute('data-tone') !== 'waiting' &&
                (element.querySelector(figure)?.textContent ?? '').trim() !== '' &&
                (element.querySelector(figure)?.textContent ?? '').trim() !== '…'
            ),
          { tile, figure }
        ),
      { timeout: 60_000 }
    )
    .toBe(true);
  const read = await tab.evaluate(
    ({ tile, figure }) => {
      const boxes = Array.from(document.querySelectorAll(tile)).map((element) => {
        const box = element.getBoundingClientRect();
        const number = element.querySelector(figure)?.getBoundingClientRect();
        return {
          left: Math.round(box.left),
          top: Math.round(box.top),
          height: Math.round(box.height),
          figure: number === undefined ? -1 : Math.round(number.top),
        };
      });
      const columns = new Set(boxes.map((box) => box.left)).size;
      const byRow = new Map<number, { tops: number[]; heights: number[] }>();
      for (const box of boxes) {
        const row = byRow.get(box.top) ?? { tops: [], heights: [] };
        row.tops.push(box.figure);
        row.heights.push(box.height);
        byRow.set(box.top, row);
      }
      return { columns, rows: Array.from(byRow.values()) };
    },
    { tile, figure }
  );
  await context.close();
  return read;
}

for (const [width, across] of [
  [390, 2],
  [1280, 4],
] as const) {
  test(`the Admin strip is ${across} across at ${width}, each row one height and its numbers on one baseline`, async ({
    browser,
  }) => {
    const strip = await rowsOf(
      browser,
      width,
      '/?view=admin&card=health',
      '[data-testid="stat-strip"] > li > a',
      '[class*="tileValue_"]'
    );
    expect(strip.columns).toBe(across);
    for (const row of strip.rows) {
      // A figure the selector missed reads -1 and would pass the baseline check vacuously.
      expect(Math.min(...row.tops)).toBeGreaterThanOrEqual(0);
      expect(Math.max(...row.tops) - Math.min(...row.tops)).toBeLessThanOrEqual(1);
      expect(Math.max(...row.heights) - Math.min(...row.heights)).toBeLessThanOrEqual(1);
    }
  });
}

test('the landing stat row sets its numbers on one baseline, the two-line label reserved', async ({
  browser,
}) => {
  for (const width of [390, 1280]) {
    const row = await rowsOf(
      browser,
      width,
      '/',
      '[data-testid="landing-proof"] > li',
      '[class*="proofFigure_"]'
    );
    for (const line of row.rows) {
      expect(Math.min(...line.tops), `a figure found at ${width}`).toBeGreaterThanOrEqual(0);
      expect(Math.max(...line.tops) - Math.min(...line.tops), `at ${width}`).toBeLessThanOrEqual(1);
    }
  }
});
// #endregion strip-rows
