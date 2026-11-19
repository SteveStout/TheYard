import { expect, test, type Page } from '@playwright/test';
import { REVIEW_STOPS } from '../../src/lib/reviewStops';
import { CURRENT_KEY, REACHED_KEY } from '../../src/lib/reviewProgress';
import { proofFigures, type TestSummary } from '../../src/lib/landingProof';
import { openTheYard } from './app';
import { fadedContent, quietWordsOnTheBareGround } from './glass';

// #region review
/**
 * The review route (ADR: Ten minutes, seven stops): its row in the rail directly under Home, the
 * first stop current on a first arrival, Next stop moving the ring and the place, the place kept
 * across a reload on this browser, Mark done on the last stop ending at the finish panel, the
 * page on the shared glass at a phone's width with nothing sideways, the two ways to reach Steven
 * on the finish panel and About, its tagged open reported under its own path, and its short door
 * keeping the code.
 */
async function freshRoute(page: Page) {
  await page.addInitScript(
    ([current, reached]) => {
      if (sessionStorage.getItem('review-spec-cleared') === null) {
        localStorage.removeItem(current);
        localStorage.removeItem(reached);
        sessionStorage.setItem('review-spec-cleared', '1');
      }
    },
    [CURRENT_KEY, REACHED_KEY]
  );
}

/** The first screen's claim (ADR: The review route gives a reason before it asks). */
const CLAIM = 'Production .NET that stays up. Built with AI, measured on every version.';

const stop = (page: Page, index: number) =>
  page.getByTestId(`review-stop-${REVIEW_STOPS[index].key}`);

test('the rail row under Home opens the review route', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openTheYard(page, '/');
  const rail = page.getByTestId('side-rail');
  const rows = (await rail.locator('button, a').allInnerTexts()).map((text) => text.trim());
  const home = rows.indexOf('Home');
  expect(home).toBeGreaterThan(-1);
  expect(rows[home + 1]).toBe('Check my work');
  expect(rows[home + 2]).toContain('Blog');
  await rail.getByRole('button', { name: 'Check my work' }).click();
  await expect(page).toHaveURL(/\?view=review$/);
  await expect(page.getByTestId('view-announcement')).toHaveText('Production .NET that stays up');
  await expect(page.getByRole('heading', { level: 1, name: CLAIM })).toBeVisible();
  await page.goBack();
  await expect(page.getByTestId('landing')).toBeVisible();
});

test('a first arrival is at stop 1, Next stop moves the place, and a reload keeps it', async ({
  page,
}) => {
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  await expect(stop(page, 0)).toHaveAttribute('data-state', 'current');
  await expect(stop(page, 0).getByTestId('review-do-this')).toBeVisible();
  await expect(page.getByTestId('review-do-this')).toHaveCount(1);
  await expect(page.getByTestId('review-progress')).toHaveAttribute('data-reached', '0');

  await page.getByRole('button', { name: 'Next stop' }).click();
  await expect(page.getByTestId('review-progress')).toHaveAttribute('data-reached', '1');
  await expect(page.getByTestId('review-ring')).toContainText('1/7');
  await expect(stop(page, 0)).toHaveAttribute('data-state', 'done');
  await expect(stop(page, 1)).toHaveAttribute('data-state', 'current');
  await expect(page.getByTestId('review-progress')).toContainText('Stop 2 of 7');

  await page.reload();
  await expect(stop(page, 1)).toHaveAttribute('data-state', 'current');
  await expect(stop(page, 0)).toHaveAttribute('data-state', 'done');
  await expect(page.getByTestId('review-ring')).toContainText('1/7');
});

test('Mark done on the last stop ends at the finish panel', async ({ page }) => {
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  const last = stop(page, REVIEW_STOPS.length - 1);
  await last.getByRole('button', { name: /The resume/ }).click();
  await expect(last).toHaveAttribute('data-state', 'current');
  await last.getByRole('button', { name: 'Mark done' }).click();
  const finish = page.getByTestId('review-finish');
  await expect(finish).toHaveAttribute('data-state', 'finished');
  await expect(finish).toBeInViewport();
  await expect(finish).toBeFocused();
  await expect(finish).toContainText('That is the work');
  await expect(last).toHaveAttribute('data-state', 'done');
});

test('the finish panel and About both say how to reach Steven, as links', async ({ page }) => {
  // profile1110-f2: a manager who finished the ten minutes had no link to write to him.
  const ways = [
    ['Email Steven', /\/api\/go\/email$/],
    ['Message me on LinkedIn', /^https:\/\/www\.linkedin\.com\/in\/stevenwstout\/?$/],
  ] as const;
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  const reach = page.getByTestId('review-reach');
  for (const [name, href] of ways)
    await expect(reach.getByRole('link', { name })).toHaveAttribute('href', href);
  await page.goto('/about');
  const about = page.getByRole('article');
  for (const [name, href] of ways)
    await expect(about.getByRole('link', { name })).toHaveAttribute('href', href);
});

test('every stop opens the site’s own address, and the stop on screen is the only one with Do this', async ({
  page,
}) => {
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  for (const [index, entry] of REVIEW_STOPS.entries()) {
    const link = stop(page, index).getByRole('link', { name: /^Open/ });
    await expect(link).toHaveAttribute('href', entry.address);
    await expect(link).not.toHaveAttribute('target', '_blank');
  }
  for (const [index, entry] of REVIEW_STOPS.entries()) {
    const record = stop(page, index).getByTestId('review-record').getByRole('link');
    await expect(record).toHaveAttribute('href', `/?doc=${entry.record}`);
    await expect(record).not.toHaveAttribute('target', '_blank');
  }
  await stop(page, 3).getByRole('button', { name: REVIEW_STOPS[3].title }).click();
  await expect(page.getByTestId('review-do-this')).toHaveCount(1);
  await expect(stop(page, 3).getByTestId('review-do-this')).toBeVisible();
});

test('a stop that names an Admin card opens with that card in view, not under the tiles (MASTER-PLAN-2026-11-17 V1)', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const named = REVIEW_STOPS.filter((each) => /[?&]card=/.test(each.address));
  expect(named.length).toBeGreaterThan(0);
  for (const each of named) {
    const card = new URLSearchParams(each.address.split('?')[1]).get('card');
    await openTheYard(page, each.address);
    const open = page.locator(`[data-testid="bench-open"][data-card="${card}"]`);
    await expect(open.locator('h2').first()).toBeInViewport({ timeout: 15_000 });
  }
  // A padded name opens the card the address handler trims it to, and scrolls to it too (CodeRabbit on 1.0.5.82).
  await openTheYard(page, '/?view=admin&card=%20spend%20');
  await expect(
    page.locator('[data-testid="bench-open"][data-card="spend"] h2').first()
  ).toBeInViewport({
    timeout: 15_000,
  });
});

test('a stop’s decision record opens its document, and Back returns to the route', async ({
  page,
}) => {
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  await stop(page, 1).getByTestId('review-record').getByRole('link').click();
  await expect(page).toHaveURL(new RegExp(`\\?doc=${REVIEW_STOPS[1].record}$`));
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.goBack();
  await expect(page.getByTestId('review-page')).toBeVisible();
});

test('at 390 nothing scrolls sideways, every control is a thumb tall, and every panel is the glass', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  const route = page.getByTestId('review-page');
  await expect(route).toBeVisible();
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBeLessThanOrEqual(0);
  const short = await route.locator('button, a').evaluateAll((controls) =>
    controls
      .map((control) => ({
        name: (control.textContent ?? '').trim(),
        height: control.getBoundingClientRect().height,
      }))
      .filter((control) => control.height < 44)
  );
  expect(short).toEqual([]);
  const panels = route.locator(
    '[data-testid^="review-stop-"], [data-testid="review-progress"], [data-testid="review-finish"]'
  );
  await expect(panels).toHaveCount(9);
  for (const panel of await panels.all()) await expect(panel).toHaveClass(/op-glass/);
  expect(await fadedContent(page)).toEqual([]);
  expect(await quietWordsOnTheBareGround(page)).toEqual([]);
});

test('a tagged link that opens the route reports its open under /review, read on the first render', async ({
  page,
}) => {
  const reports: string[] = [];
  await page.route('**/api/links/open**', async (route) => {
    reports.push(route.request().url());
    await route.fulfill({ status: 204 });
  });
  await openTheYard(page, '/?view=review&r=zzrev1');
  await expect.poll(() => reports.length).toBe(1);
  expect(new URL(reports[0]).searchParams.get('path')).toBe('/review');
  expect(new URL(reports[0]).searchParams.get('r')).toBe('zzrev1');
  // The address bar keeps the view, so a reload stays on the route.
  await expect(page).toHaveURL(/\?view=review/);
});

test('the short door /review is a permanent redirect that keeps the code', async ({ request }) => {
  const response = await request.get('/review?r=zzrev1', { maxRedirects: 0 });
  expect(response.status()).toBe(301);
  expect(response.headers()['location']).toBe('/?view=review&r=zzrev1');
});
// #endregion review

/** The bottom edge of an element on the page, read where it is drawn. */
async function bottomOf(page: Page, testId: string): Promise<number> {
  const box = await page.getByTestId(testId).boundingBox();
  expect(box, `${testId} is drawn`).not.toBeNull();
  return box!.y + box!.height;
}

// The first screen (ADR: The review route gives a reason before it asks): at the desk the screenshot was taken at,
// the claim, the three tiles, the one quote and both buttons with no scroll, the tour and its ten minutes under the
// fold, the store bar's one sentence in place of the toggle, the ring and the chips, and the sidebar collapsed on a
// first visit with Menu, the rail's expand button, opening it. The live tile prints the landing page's own figure.
test('at 1568 by 772 the first screen gives the reason before the ask, with no scroll and no chrome for the operator', async ({
  page,
}) => {
  const summary = (await (await page.request.get('/api/tests/summary')).json()) as TestSummary;
  const landing = proofFigures(summary, 0)!.find((figure) => figure.key === 'tests')!.figure;
  await page.setViewportSize({ width: 1568, height: 772 });
  await freshRoute(page);
  await page.addInitScript(() => localStorage.removeItem('theyard.rail'));
  await openTheYard(page, '/?view=review');
  await expect(page.getByRole('heading', { level: 1, name: CLAIM })).toBeVisible();
  await expect(page.getByTestId('review-proof-tests-figure')).toHaveText(landing);
  await expect(page.getByTestId('review-lead-quote')).toContainText('Drew Dell');
  for (const shown of [
    'review-proof-releases',
    'review-proof-cycle',
    'review-proof-tests',
    'review-lead-quote',
    'review-proof',
    'review-book',
  ])
    expect(await bottomOf(page, shown), shown).toBeLessThanOrEqual(772);
  // The ten-minute ask is the tour's, under the fold.
  const tour = await page.getByTestId('review-tour').boundingBox();
  expect(tour!.y).toBeGreaterThan(772);
  await expect(page.getByTestId('review-book')).toHaveAttribute('href', '/api/go/book');
  // The band says one sentence to a stranger.
  await expect(page.getByTestId('store-bar-live')).toContainText('Live system.');
  await expect(page.getByTestId('gate-badge')).toHaveCount(0);
  await expect(page.getByTestId('store-bar-ready')).toHaveCount(0);
  // The sidebar is collapsed on a first visit, and Menu opens it.
  const frame = page.locator('[data-rail]').first();
  await expect(frame).toHaveAttribute('data-rail', 'collapsed');
  await page.getByRole('button', { name: 'Expand the sidebar' }).click();
  await expect(frame).toHaveAttribute('data-rail', 'open');
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBeLessThanOrEqual(0);
});

test('at 390 by 844 the primary button is on the first screen and nothing scrolls sideways', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  await expect(page.getByRole('heading', { level: 1, name: CLAIM })).toBeVisible();
  expect(await bottomOf(page, 'review-proof')).toBeLessThanOrEqual(844);
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBeLessThanOrEqual(0);
});

// The mirror (ADR: The review route gives a reason before it asks, the addendum on the mirror): only for=ai draws it,
// with its gate panel and its names (its own link preview is render.test.ts's), the address keeps for=ai, and every
// report names the screen.
test('for=ai draws the mirror with its gate panel at 1568 by 772, keeps the address, and reports its screen', async ({
  page,
}) => {
  const screens: string[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { screen?: string };
    screens.push(body.screen ?? '');
    await route.fulfill({ status: 204 });
  });
  await page.setViewportSize({ width: 1568, height: 772 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review&for=ai');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Shipping with AI without losing quality.' })
  ).toBeVisible();
  await expect(page).toHaveURL(/[?&]for=ai/);
  for (const shown of ['review-proof', 'review-book', 'review-gate-panel'])
    expect(await bottomOf(page, shown), shown).toBeLessThanOrEqual(772);
  await expect(page.getByTestId('review-proof')).toHaveAttribute('href', '/?view=admin&card=tests');
  await expect(page.getByTestId('review-names')).toContainText('Drew Dell');
  await expect.poll(() => screens).toContain('mirror');
  expect(screens.every((screen) => screen === 'mirror')).toBe(true);
  // At 390 the primary button is still on the first screen.
  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page, '/?view=review&for=ai');
  expect(await bottomOf(page, 'review-proof')).toBeLessThanOrEqual(844);
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBeLessThanOrEqual(0);
});

// "See the proof in 2 minutes" opens Quality in place on "The slip, and the turn", and reports its action once.
test('the primary button opens Quality on the slip and the turn, in place, and reports the proof action', async ({
  page,
}) => {
  const actions: string[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { action?: string };
    if (body.action) actions.push(body.action);
    await route.fulfill({ status: 204 });
  });
  await page.setViewportSize({ width: 1568, height: 772 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  let loads = 0;
  page.on('load', () => (loads += 1));
  await page.getByTestId('review-proof').click();
  const dialog = page.locator('dialog[open]');
  const slip = dialog.locator('h2[id="the-slip-and-the-turn"]');
  await expect(slip).toHaveText('The slip, and the turn', { timeout: 30_000 });
  await expect(slip).toBeInViewport();
  await expect(page).toHaveURL(/[?&]doc=quality/);
  expect(loads).toBe(0);
  await expect.poll(() => actions).toEqual(['proof']);
});
