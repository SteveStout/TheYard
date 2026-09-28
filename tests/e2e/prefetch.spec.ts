import { expect, test, type Page, type Request } from '@playwright/test';
import { openTheYard } from './app';

/**
 * Clicks that never wait on code (ADR: Code that reads like code, addendum of
 * 28 September). Measured on the live site that morning, a document was a
 * waterfall: the markdown finished at 775 ms and only then did the renderer's
 * chunk start, at 781. Now the two start together, and on an idle page the
 * renderer and the Admin tab are fetched before anyone asks for them.
 */

/** The renderer's chunk: a hashed file under the preview build, the source module under the dev server. */
const isRenderer = (request: Request) =>
  /\/(assets\/markdown-[\w-]+\.js|src\/lib\/markdown\.ts)/.test(request.url());
const isAdminTab = (request: Request) =>
  /\/(assets\/AdminPanel-[\w-]+\.js|src\/components\/admin\/AdminPanel\/AdminPanel\.tsx)/.test(
    request.url()
  );

/** A reader who asked the browser to save data: the page fetches nothing ahead for them. */
async function savingData(page: Page): Promise<void> {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'connection', {
      configurable: true,
      value: { saveData: true, effectiveType: '4g' },
    });
  });
}

test('opening a document asks for the document and its renderer together, not one after the other', async ({
  page,
}) => {
  await savingData(page);
  // The document is held back half a second, so a renderer that waited for it
  // would start after it finished and the overlap below could not hold.
  await page.route('**/api/docs/readme', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 500));
    await route.continue();
  });
  const seen: { document?: Request; renderer?: Request } = {};
  page.on('request', (request) => {
    if (request.url().endsWith('/api/docs/readme')) seen.document = request;
    if (isRenderer(request)) seen.renderer ??= request;
  });

  await openTheYard(page);
  expect(
    seen.renderer,
    'nothing fetches the renderer ahead when the reader saves data'
  ).toBeUndefined();
  await openTheYard(page, '/?doc=readme');
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect.poll(() => seen.document !== undefined && seen.renderer !== undefined).toBe(true);
  await seen.document!.response();

  const document = seen.document!.timing();
  const renderer = seen.renderer!.timing();
  // Both timings are from the browser's own clock: the renderer's request began
  // before the document's response had ended.
  expect(renderer.startTime).toBeLessThan(document.startTime + document.responseEnd);
});

test('an idle page fetches the renderer and the Admin tab before anyone asks for them', async ({
  page,
}) => {
  // Every other spec opens the page saving data (app.ts); this one asks for the prefetch back.
  await page.addInitScript(() => {
    (window as unknown as { __yardPrefetch?: boolean }).__yardPrefetch = true;
  });
  const ahead: string[] = [];
  page.on('request', (request) => {
    if (isRenderer(request)) ahead.push('renderer');
    if (isAdminTab(request)) ahead.push('admin');
  });
  await openTheYard(page);
  await expect
    .poll(() => [...new Set(ahead)].sort(), { timeout: 15_000 })
    .toEqual(['admin', 'renderer']);
});
