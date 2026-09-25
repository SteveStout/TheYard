import { expect, test, type Browser, type APIRequestContext } from '@playwright/test';
import { readPage, siteList, type SitePage } from './coverage';

/**
 * Every page the same, none missed (ADR: The glass look, the addendum on every
 * page the same). One test per width: every page the site lists for itself is
 * opened and read in the browser, and a page passes when its body is IBM Plex
 * Sans, it draws at least one panel, every panel carries the brackets and the
 * 3 px rule, no button is square, no word is set in another face (code
 * excepted), no id is used twice and no drawing's reference is lost. StyleRulesTests rule nine is the static half: a sheet that forgot
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
        await tab.goto(next.address, { waitUntil: 'networkidle' });
        // An app view has settled when its announcement names it; a page of its own has drawn
        // itself when its script has run (the API reference renders after its script loads).
        if (!next.address.startsWith('/api/')) {
          await expect(tab.getByTestId('view-announcement')).not.toHaveText(/Loading/, {
            timeout: 60_000,
          });
          // An Admin card is a chunk of its own: the tab has drawn when the workbench is on the
          // page and no card is still on its way (a phone read the log card's address before
          // either, 1394).
          if (next.address.includes('view=admin')) {
            await expect(tab.getByTestId('workbench')).toBeVisible({ timeout: 30_000 });
            await expect(tab.getByTestId('bench-loading')).toHaveCount(0, { timeout: 30_000 });
          }
          await tab.waitForTimeout(300);
        } else {
          // The API reference draws itself from a script of its own, which on a cold phone took
          // longer than the second and a half that was waited here at first (1393).
          await tab
            .waitForFunction(
              () => document.querySelector('.op-glass, .scalar-card') !== null,
              null,
              {
                timeout: 30_000,
              }
            )
            .catch(() => undefined);
          await tab.waitForTimeout(500);
        }
        const facts = await tab.evaluate(readPage);
        const wrong = [
          facts.font.includes('IBM Plex Sans') ? '' : `the body is ${facts.font}`,
          facts.panels > 0 ? '' : 'no panel',
          facts.unstyled.length ? `unstyled ${facts.unstyled.join(', ')}` : '',
          facts.square.length ? `square ${facts.square.join(', ')}` : '',
          facts.wrongFace.length ? `another face ${facts.wrongFace.join(', ')}` : '',
          facts.twice.length ? `an id used twice ${facts.twice.join(', ')}` : '',
          facts.lost.length ? `a drawing's reference lost ${facts.lost.join(', ')}` : '',
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
