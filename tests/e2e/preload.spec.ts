import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The API reads started from the head (ADR: The landing page rendered at build
 * time, server rendering as the goal). index.html hints each read the first page
 * makes, so it starts while the script is still arriving, and the fetch that asks
 * for it later is handed the answer already here. A hint that does not match its
 * fetch exactly is worse than none: the browser asks for the address twice and
 * says so in the console. These hold both halves: every hinted address is asked
 * for once, and the browser reports no hint it could not use.
 */

/** The reads every view makes on its first load. */
const EVERY_VIEW = ['/api/stores', '/api/auth/me', '/api/bids', '/api/version'];
/** The two only the landing page makes, hinted only when the address carries no query. */
const LANDING_ONLY = ['/api/health', '/api/tests/summary'];

/** Every API address the page asked for over the network, with its query, in order. */
function watchApiReads(page: Page): string[] {
  const asked: string[] = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname.startsWith('/api/')) asked.push(url.pathname + url.search);
  });
  return asked;
}

/** What the browser says about a hint it could not use: credentials that differ, or a hint never used. */
function watchHintWarnings(page: Page): string[] {
  const warnings: string[] = [];
  page.on('console', (message) => {
    if (/preload/i.test(message.text())) warnings.push(message.text());
  });
  return warnings;
}

/** The fetch hints in the head, as the addresses they name. */
const hintsInTheHead = (page: Page) =>
  page.evaluate(() =>
    [...document.querySelectorAll<HTMLLinkElement>('link[rel="preload"][as="fetch"]')].map(
      (link) => ({
        address: new URL(link.href).pathname,
        crossOrigin: link.crossOrigin,
      })
    )
  );

/**
 * The dev server mounts every component twice under StrictMode, on purpose, so
 * there each read is asked for twice whatever the hints do. The built page, which
 * the gate's browser pass opens, mounts once.
 */
const builtPage = (page: Page) =>
  page.evaluate(() => document.querySelector('script[src="/src/main.tsx"]') === null);

/** Addresses asked for more than once. */
const twice = (asked: string[]) => [...new Set(asked.filter((a, i) => asked.indexOf(a) !== i))];

test('the landing page hints its six API reads in the head, and asks for each one once', async ({
  page,
}) => {
  const asked = watchApiReads(page);
  const warnings = watchHintWarnings(page);
  await openTheYard(page, '/');
  // Both of the landing page's own reads have drawn: the health dot and the gate's counts.
  await expect(page.getByTestId('landing-admin-health')).toHaveText(/^(Healthy|Degraded)$/);
  await expect(page.getByTestId('landing-proof-tests')).toContainText(/\d[\d,]* *tests/);

  const hints = await hintsInTheHead(page);
  expect(hints.map((hint) => hint.address).sort()).toEqual([...EVERY_VIEW, ...LANDING_ONLY].sort());
  for (const hint of hints) expect(hint.crossOrigin, hint.address).toBe('anonymous');
  await expect
    .poll(() => hints.every((hint) => asked.includes(hint.address)), { timeout: 15_000 })
    .toBe(true);

  // A hint nobody used is reported a few seconds after the load event; wait past it.
  await page.waitForTimeout(4_000);
  expect(warnings, 'the browser reports no hint it could not use').toEqual([]);
  if (await builtPage(page)) {
    expect(twice(asked), 'no API address is asked for twice').toEqual([]);
  }
});

test('a page that opens on another view hints only the reads every view makes', async ({
  page,
}) => {
  const asked = watchApiReads(page);
  const warnings = watchHintWarnings(page);
  await openTheYard(page, '/?view=inventory');

  const hints = await hintsInTheHead(page);
  expect(hints.map((hint) => hint.address).sort()).toEqual([...EVERY_VIEW].sort());
  await expect
    .poll(() => hints.every((hint) => asked.includes(hint.address)), { timeout: 15_000 })
    .toBe(true);

  await page.waitForTimeout(4_000);
  expect(warnings, 'the browser reports no hint it could not use').toEqual([]);
  if (await builtPage(page)) {
    for (const address of EVERY_VIEW) {
      expect(
        asked.filter((a) => a === address),
        `${address} is asked for once`
      ).toHaveLength(1);
    }
  }
});
