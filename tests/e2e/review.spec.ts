import { expect, test, type Page } from '@playwright/test';
import { REVIEW_STOPS } from '../../src/lib/reviewStops';
import { CURRENT_KEY, REACHED_KEY } from '../../src/lib/reviewProgress';
import { proofFigures, type TestSummary } from '../../src/lib/landingProof';
import { cardAddress } from '../../src/lib/recommendationsPage';
import { VOICES } from '../../src/lib/voices';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';
import { fadedContent, quietWordsOnTheBareGround } from './glass';

// #region review
/**
 * The review route (ADR: Ten minutes, seven stops, and the addendum on one unit): its row in the rail
 * directly under Home, the tour's eight compact cells, a cell opened counted and ticked and kept across
 * a reload on this browser, Start over clearing it, the close with its three buttons, the page on the
 * shared glass at a phone's width with nothing sideways, the ways to reach Steven on the close and
 * About, its tagged open reported under its own path, and its short door keeping the code.
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

/** Three made-up posts, so the review route's blog row is drawn whatever the local store holds. */
const POSTS = [
  made('a-made-up-newest-post', 'A made-up newest post', 1),
  made('a-made-up-second-post', 'A made-up second post', 3),
  made('a-made-up-third-post', 'A made-up third post', 6),
];

const cell = (page: Page, index: number) =>
  page.getByTestId(`review-tour-${REVIEW_STOPS[index].key}`);

test('the rail row under Home opens the review route', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openTheYard(page, '/');
  const rail = page.getByTestId('side-rail');
  const rows = (await rail.locator('button, a').allInnerTexts()).map((text) => text.trim());
  const home = rows.indexOf('Home');
  expect(home).toBeGreaterThan(-1);
  expect(rows[home + 1]).toBe('Check my work');
  expect(rows[home + 2]).toContain('Blog');
  expect(rows).toContain('Sign in');
  expect(rows).toContain('Admin');
  await rail.getByRole('button', { name: 'Check my work' }).click();
  await expect(page).toHaveURL(/\?view=review$/);
  await expect(page.getByTestId('view-announcement')).toHaveText('Production .NET that stays up');
  await expect(page.getByRole('heading', { level: 1, name: CLAIM })).toBeVisible();
  // A stranger reading the route is not shown Sign in or Admin; Home, the doors and the links out stay.
  await expect(rail.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(0);
  await expect(rail.getByRole('button', { name: 'Admin', exact: true })).toHaveCount(0);
  for (const kept of ['Home', 'Check my work', 'Inventory'])
    await expect(rail.getByRole('button', { name: kept, exact: true })).toHaveCount(1);
  await page.goBack();
  await expect(page.getByTestId('landing')).toBeVisible();
  await expect(rail.getByRole('button', { name: 'Admin', exact: true })).toHaveCount(1);
});

test('a tour cell opened counts once, ticks its ring and is kept across a reload; Start over clears the place', async ({
  page,
}) => {
  const stops: number[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { stop?: number };
    if (typeof body.stop === 'number' && body.stop > 0) stops.push(body.stop);
    await route.fulfill({ status: 204 });
  });
  await page.setViewportSize({ width: 1440, height: 900 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  await expect(page.getByTestId('review-tour').locator('li')).toHaveCount(8);
  await expect(page.getByTestId('review-tour-seen')).toHaveText('0 of 7 seen.');
  await expect(cell(page, 1)).toHaveAttribute('data-reached', 'false');

  await cell(page, 1).click();
  await expect(page).toHaveURL(/\/$/);
  await page.goBack();
  await expect(cell(page, 1)).toHaveAttribute('data-reached', 'true');
  await expect(page.getByTestId('review-tour-seen')).toHaveText('1 of 7 seen.');
  await expect.poll(() => stops).toEqual([2]);

  await page.reload();
  await expect(cell(page, 1)).toHaveAttribute('data-reached', 'true');
  await page.getByRole('button', { name: 'Start over' }).click();
  await expect(page.getByTestId('review-tour-seen')).toHaveText('0 of 7 seen.');
  await expect(cell(page, 1)).toHaveAttribute('data-reached', 'false');
});

test('the close carries Anthony Fortner’s voice, and the close and About both say how to reach Steven, as links', async ({
  page,
}) => {
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  const close = page.getByTestId('review-close');
  await expect(close).toContainText('That is the work');
  await expect(close.getByTestId('voice')).toHaveAttribute('data-name', 'Anthony Fortner');
  await expect(close.getByTestId('voice')).toHaveAttribute('href', cardAddress('Anthony Fortner'));
  await expect(close).not.toContainText('Sean Whitesell');
  await expect(close.getByRole('link', { name: 'Book 20 minutes' })).toHaveAttribute(
    'href',
    '/api/go/book'
  );
  await expect(close.getByRole('link', { name: 'Resume (PDF)' })).toHaveAttribute(
    'href',
    '/resume?pdf=1'
  );
  await expect(close.getByRole('link', { name: 'Message me on LinkedIn' })).toHaveAttribute(
    'href',
    /^https:\/\/www\.linkedin\.com\/in\/stevenwstout\/?$/
  );
  await expect(close.getByRole('link', { name: 'Email Steven' })).toHaveCount(0);
  // profile1110-f2: a manager who finished the ten minutes had no link to write to him; About keeps both.
  await page.goto('/about');
  const about = page.getByRole('article');
  await expect(about.getByRole('link', { name: 'Email Steven' })).toHaveAttribute(
    'href',
    /\/api\/go\/email$/
  );
  await expect(about.getByRole('link', { name: 'Message me on LinkedIn' })).toHaveAttribute(
    'href',
    /^https:\/\/www\.linkedin\.com\/in\/stevenwstout\/?$/
  );
});

test('every tour cell opens the site’s own address in place, and the last opens the sidebar', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await freshRoute(page);
  await page.addInitScript(() => localStorage.removeItem('theyard.rail'));
  await openTheYard(page, '/?view=review');
  for (const [index, entry] of REVIEW_STOPS.entries()) {
    await expect(cell(page, index)).toHaveAttribute('href', entry.address);
    await expect(cell(page, index)).not.toHaveAttribute('target', '_blank');
    await expect(cell(page, index)).toContainText(entry.title);
  }
  const frame = page.locator('[data-rail]').first();
  await expect(frame).toHaveAttribute('data-rail', 'collapsed');
  await page.getByTestId('review-tour-rest').click();
  await expect(frame).toHaveAttribute('data-rail', 'open');
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

test('a tour cell that names a document opens it in place, and Back returns to the route', async ({
  page,
}) => {
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  const stores = REVIEW_STOPS.findIndex((each) => each.key === 'stores');
  await cell(page, stores).click();
  await expect(page).toHaveURL(/\?doc=sql-vs-cosmos$/);
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
  // The tour's cells are closed behind its control on a phone; opened, every one is a thumb tall too.
  await page.getByTestId('review-tour-toggle').click();
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
  // The three stops, the eight tour cells and the close.
  const panels = route.locator(
    '[data-testid$="-stop"][data-testid^="review-path-"], [data-testid^="review-tour-"]:not([data-testid="review-tour-seen"]):not([data-testid="review-tour-toggle"]), [data-testid="review-close"]'
  );
  await expect(panels).toHaveCount(12);
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
  // The ten-minute ask is the tour's, under the fold; the two-minute path's heading is on the first screen.
  const tour = await page.getByTestId('review-tour').boundingBox();
  expect(tour!.y).toBeGreaterThan(772);
  await expect(
    page.getByRole('heading', { level: 2, name: 'Three questions, two minutes' })
  ).toBeInViewport();
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

// The short path (ADR: The review route gives a reason before it asks, the addendum on the short path): under both
// first screens, three stops with their live tiles and "0 of 3 opened.", and no visit asks the catalogue or the bids for
// the lot: it comes from the one kept answer. The shell's own read of this visitor's bid badges (useBids, one GET
// /api/bids on every page) may land while the page loads; it is the only bids read, and never more than one.
for (const address of ['/?view=review', '/?view=review&for=ai']) {
  test(`${address} draws the three-stop path with live tiles and never reads the catalogue for its lot`, async ({
    page,
  }) => {
    const asked: string[] = [];
    page.on('request', (request) => asked.push(new URL(request.url()).pathname));
    await page.setViewportSize({ width: 1568, height: 772 });
    await freshRoute(page);
    await page.addInitScript(() => localStorage.removeItem('theyard-review-short'));
    await openTheYard(page, address);
    const path = page.getByTestId('review-path');
    await expect(path.getByTestId('review-path-count')).toHaveText('0 of 3 opened.');
    await expect(path.getByTestId('review-path-spark').locator('path')).toHaveCount(5);
    await expect(path.getByTestId('review-path-total')).toHaveText(/^\d{1,3}(,\d{3})*$/);
    await expect(path.getByTestId('review-path-gate-tile')).toContainText('passed');
    await expect(path.getByTestId('review-path-room')).toHaveAttribute(
      'href',
      /^\/\?(vehicle=|view=inventory)/
    );
    // Each card asks the reader's question and names its own door; the gate's run on GitHub is drawn only with a run.
    await expect(path.getByRole('heading', { level: 3 })).toHaveText([
      /How do you keep quality when AI writes the code\?/,
      /What has to pass before anything ships\?/,
      /Is this a real system, running right now\?/,
    ]);
    await expect(path.getByTestId('review-path-quality')).toHaveText(/See the slip and the fix/);
    await expect(path.getByTestId('review-path-quality')).toHaveAttribute('href', '/?doc=quality');
    await expect(path.getByTestId('review-path-gate')).toHaveText(/See the test results/);
    await expect(path.getByTestId('review-path-room')).toHaveText(/Watch this lot live/);
    await expect(path.getByText('Open this stop')).toHaveCount(0);
    const run = path.getByTestId('review-path-gate-run');
    if ((await run.count()) > 0)
      await expect(run).toHaveAttribute(
        'href',
        /^https:\/\/github\.com\/SteveStout\/TheYard\/actions\/runs\/\d+$/
      );
    expect(asked).toContain('/api/review/lot');
    expect(asked.filter((each) => each.startsWith('/api/vehicles'))).toEqual([]);
    expect(asked.filter((each) => each.startsWith('/api/bids/'))).toEqual([]);
    expect(asked.filter((each) => each === '/api/bids').length).toBeLessThanOrEqual(1);
  });
}

// On a phone the three stops stack, nothing scrolls sideways and every button is a thumb tall; the Quality stop opens
// the top of the Quality page in place, reports its action, and the path counts it on this browser.
test('at 390 the short path stacks, and its Quality stop opens the top of Quality and counts once', async ({
  page,
}) => {
  const actions: string[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { action?: string };
    if (body.action) actions.push(body.action);
    await route.fulfill({ status: 204 });
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await freshRoute(page);
  await page.addInitScript(() => {
    if (sessionStorage.getItem('path-spec-cleared') === null) {
      localStorage.removeItem('theyard-review-short');
      sessionStorage.setItem('path-spec-cleared', '1');
    }
  });
  await openTheYard(page, '/?view=review');
  const quality = page.getByTestId('review-path-quality');
  const room = page.getByTestId('review-path-room');
  const first = await quality.boundingBox();
  const third = await room.boundingBox();
  expect(third!.y).toBeGreaterThan(first!.y + first!.height);
  for (const button of [quality, page.getByTestId('review-path-gate'), room])
    expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBeLessThanOrEqual(0);

  await quality.click();
  const top = page.locator('dialog[open]').locator('h2').first();
  await expect(top).toHaveText('Quality you can watch', { timeout: 30_000 });
  await expect(top).toBeInViewport();
  await expect.poll(() => actions).toEqual(['stop-quality']);
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('review-path-count')).toHaveText('1 of 3 opened.');
  await page.reload();
  await expect(page.getByTestId('review-path-count')).toHaveText('1 of 3 opened.');
});

// The mirror's honest line opens Quality in place on the slip and the turn and reports its own action; its Book 20
// minutes is the counted mailto, the same as the verdict's.
test('the mirror’s slip line lands on the slip and the turn and reports slip', async ({ page }) => {
  const actions: string[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { action?: string };
    if (body.action) actions.push(body.action);
    await route.fulfill({ status: 204 });
  });
  await page.setViewportSize({ width: 1568, height: 772 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review&for=ai');
  await expect(page.getByTestId('review-book')).toHaveAttribute('href', '/api/go/book');
  await page.getByTestId('review-slip').click();
  const slip = page.locator('dialog[open]').locator('h2[id="the-slip-and-the-turn"]');
  await expect(slip).toBeInViewport({ timeout: 30_000 });
  await expect.poll(() => actions).toEqual(['slip']);
});

// "See the proof in 2 minutes" opens the top of Quality in place, and reports its action once.
test('the primary button opens the top of Quality, in place, and reports the proof action', async ({
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
  await expect(page.getByTestId('review-proof')).toHaveAttribute('href', '/?doc=quality');
  await page.getByTestId('review-proof').click();
  const dialog = page.locator('dialog[open]');
  const top = dialog.locator('h2').first();
  await expect(top).toHaveText('Quality you can watch', { timeout: 30_000 });
  await expect(top).toBeInViewport();
  await expect(page).toHaveURL(/[?&]doc=quality/);
  expect(loads).toBe(0);
  await expect.poll(() => actions).toEqual(['proof']);
});

// #region the-look
// The look is an exit criterion (RULE 82; ADR: The review route gives a reason before it asks, the addendum on one
// unit): the rules read off the whole-page picture (J:\mentor\reviewscreen\FULLPAGE-METRICS.json holds the numbers
// behind each), held at every width the picture was measured at, for both addresses. A rule, never a typed figure.
const LOOK_WIDTHS = [
  [1920, 1080],
  [1568, 772],
  [1440, 900],
  [1280, 800],
  [1024, 768],
  [390, 844],
] as const;

type Box = { top: number; bottom: number; left: number; right: number; height: number };

/** The page's direct sections, top to bottom, on the page, with what each holds. */
async function sections(page: Page) {
  return page.getByTestId('review-page').evaluate((route) =>
    [...route.children].map((section) => {
      const box = section.getBoundingClientRect();
      return {
        top: box.top + window.scrollY,
        bottom: box.bottom + window.scrollY,
        left: box.left,
        right: box.right,
        height: box.height,
        next: section.querySelector('[data-testid="where-next"]') !== null,
        names: section.querySelector('[data-testid="review-names"]') !== null,
        content:
          Math.max(...[...section.children].map((child) => child.getBoundingClientRect().bottom)) -
          box.top,
      };
    })
  );
}

/** Each matching element's box on the page. */
async function boxes(page: Page, selector: string): Promise<Box[]> {
  return page.locator(selector).evaluateAll((all) =>
    all.map((each) => {
      const box = each.getBoundingClientRect();
      return {
        top: box.top + window.scrollY,
        bottom: box.bottom + window.scrollY,
        left: box.left,
        right: box.right,
        height: box.height,
      };
    })
  );
}

for (const address of ['/?view=review', '/?view=review&for=ai']) {
  test(`${address} is one unit at every width: shared edges, no forced height, the picture's gaps and grids`, async ({
    page,
  }) => {
    test.setTimeout(150_000);
    await freshRoute(page);
    await answerBlog(page, POSTS);
    for (const [width, height] of LOOK_WIDTHS) {
      await page.setViewportSize({ width, height });
      await openTheYard(page, address);
      await expect(page.getByTestId('review-close')).toBeVisible();
      const at = `${address} at ${width}`;
      const parts = await sections(page);
      // 1. One unit: every section has the page's left and right edge.
      for (const part of parts) {
        expect(Math.abs(part.left - parts[0].left), `${at}: left edge`).toBeLessThanOrEqual(1);
        expect(Math.abs(part.right - parts[0].right), `${at}: right edge`).toBeLessThanOrEqual(1);
      }
      // 2. No forced height: 48 between sections (32 on a phone), the names 24 under the mirror, Where next 16.
      for (const [index, part] of parts.slice(1).entries()) {
        const gap = part.top - parts[index].bottom;
        const want = part.next ? 16 : part.names ? 24 : width < 640 ? 32 : 48;
        expect(
          Math.abs(gap - want),
          `${at}: gap above section ${index + 2} is ${gap}`
        ).toBeLessThanOrEqual(2);
      }
      // 4. Three stops: one row of one height from 1280, each tile 196 tall; stacked under it.
      const cards = await boxes(page, '[data-testid^="review-path-"][data-testid$="-stop"]');
      const tiles = await boxes(
        page,
        '[data-testid="review-path-trend"], [data-testid="review-path-gate-tile"], [data-testid="review-path-lot"]'
      );
      expect(cards).toHaveLength(3);
      if (width >= 1280) {
        for (const card of cards) {
          expect(Math.abs(card.top - cards[0].top), `${at}: cards share a row`).toBeLessThanOrEqual(
            1
          );
          expect(
            Math.abs(card.height - cards[0].height),
            `${at}: cards share a height`
          ).toBeLessThanOrEqual(1);
        }
        for (const tile of tiles)
          expect(Math.abs(tile.height - 196), `${at}: tile height`).toBeLessThanOrEqual(1);
      } else {
        for (const [index, card] of cards.slice(1).entries())
          expect(card.top, `${at}: cards stack`).toBeGreaterThan(cards[index].bottom);
      }
      // 4. The three voices: from 1280 the next row, one height, each on its card's edges; under it each after its card.
      const voices = await boxes(page, '[data-testid="review-path"] [data-testid="voice"]');
      expect(voices, `${at}: a voice under each card`).toHaveLength(3);
      for (const [index, voice] of voices.entries()) {
        const card = cards[index];
        expect(voice.top, `${at}: voice after its card`).toBeGreaterThan(card.bottom);
        if (width < 1280 && index < 2)
          expect(voice.bottom, `${at}: voice before the next card`).toBeLessThan(
            cards[index + 1].top
          );
        if (width >= 1280) {
          expect(
            Math.abs(voice.top - voices[0].top),
            `${at}: voices share a row`
          ).toBeLessThanOrEqual(1);
          expect(
            Math.abs(voice.height - voices[0].height),
            `${at}: voices share a height`
          ).toBeLessThanOrEqual(1);
          expect(Math.abs(voice.left - card.left), `${at}: voice's left edge`).toBeLessThanOrEqual(
            1
          );
          expect(
            Math.abs(voice.right - card.right),
            `${at}: voice's right edge`
          ).toBeLessThanOrEqual(1);
        }
      }
      // 5. The pair and the posts: one row of one height from 1024; stacked under it.
      for (const [selector, count] of [
        ['[data-testid="review-voices"] [data-testid="voice"]', 2],
        ['[data-testid="review-blog-post"]', 3],
      ] as const) {
        const row = await boxes(page, selector);
        expect(row, `${at}: ${selector}`).toHaveLength(count);
        for (const [index, each] of row.entries()) {
          if (width >= 1024) {
            expect(Math.abs(each.top - row[0].top), `${at}: one row`).toBeLessThanOrEqual(1);
            expect(Math.abs(each.height - row[0].height), `${at}: one height`).toBeLessThanOrEqual(
              1
            );
          } else if (index > 0)
            expect(each.top, `${at}: stacked`).toBeGreaterThan(row[index - 1].bottom);
        }
      }
      // 7. On a phone no tour cell is drawn until the control is pressed, then eight are; from 640 up there is no control.
      const toggle = page.getByTestId('review-tour-toggle');
      if (width < 640) {
        await expect(page.locator('[data-testid="review-tour"] li:visible')).toHaveCount(0);
        await expect(toggle).toHaveAttribute('aria-expanded', 'false');
        await toggle.click();
        await expect(toggle).toHaveAttribute('aria-expanded', 'true');
        await expect(page.locator('[data-testid="review-tour"] li:visible')).toHaveCount(8);
      } else await expect(toggle).toBeHidden();
      // 5. Tour: eight cells, four across from 1024, one on a phone; a row shares one height; none over 140 at a desk.
      const cells = await boxes(page, '[data-testid="review-tour"] li');
      expect(cells).toHaveLength(8);
      const across = cells.filter((each) => Math.abs(each.top - cells[0].top) <= 1).length;
      expect(across, `${at}: cells across`).toBe(width >= 1024 ? 4 : width >= 640 ? 2 : 1);
      for (const each of cells) {
        const row = cells.filter((other) => Math.abs(other.top - each.top) <= 1);
        for (const other of row)
          expect(
            Math.abs(other.height - each.height),
            `${at}: a row shares a height`
          ).toBeLessThanOrEqual(1);
        if (width >= 1024) expect(each.height, `${at}: a cell's height`).toBeLessThanOrEqual(140);
      }
      // 9. Gone: no tall stop card, no ring of seven; the first screen is no taller than its content.
      for (const gone of ['Do this', 'Mark done', 'Stop 1 of 7'])
        await expect(page.getByText(gone, { exact: false })).toHaveCount(0);
      if (width === 1920)
        expect(
          parts[0].height,
          `${at}: the first screen holds no forced height`
        ).toBeLessThanOrEqual(parts[0].content + 2);
      // 7. Nothing scrolls sideways at any width.
      const sideways = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth
      );
      expect(sideways, `${at}: sideways`).toBeLessThanOrEqual(0);
      // 3. Height: under 2,750 at 1568 for the verdict, under 2,950 for the mirror (the pictures: 2,478 and 2,658).
      if (width === 1568) {
        const tall = await page.evaluate(() => document.documentElement.scrollHeight);
        expect(tall, `${at}: page height`).toBeLessThan(address.includes('for=ai') ? 2950 : 2750);
      }
      // 3. At 390 with the tour closed: under 5,600 for the verdict, under 6,200 for the mirror (5,272 and 5,851).
      if (width === 390) {
        await page.getByTestId('review-tour-toggle').click();
        const tall = await page.evaluate(() => document.documentElement.scrollHeight);
        expect(tall, `${at}: page height`).toBeLessThan(address.includes('for=ai') ? 6200 : 5600);
      }
      // 6 and 7. The primary button is on the first screen at 1568 by 772 and at 390 by 844.
      if (width === 1568 || width === 390)
        expect(
          await bottomOf(page, 'review-proof'),
          `${at}: the primary button`
        ).toBeLessThanOrEqual(height);
    }
    // 5. Two across between 640 and 1024.
    await page.setViewportSize({ width: 768, height: 1024 });
    await openTheYard(page, address);
    const cells = await boxes(page, '[data-testid="review-tour"] li');
    expect(cells.filter((each) => Math.abs(each.top - cells[0].top) <= 1)).toHaveLength(2);
  });
}

// 6. At 1568 by 772 the verdict's primary button and the path's heading are both on the first screen. (The mirror's
// picture puts its name strip first, so its path heading is under the fold there; its button is held above.)
test('at 1568 by 772 the primary button and "Three questions, two minutes" are on the first screen', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1568, height: 772 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  expect(await bottomOf(page, 'review-proof')).toBeLessThanOrEqual(772);
  await expect(
    page.getByRole('heading', { level: 2, name: 'Three questions, two minutes' })
  ).toBeInViewport();
});

// 8. Alive: on the seeded catalogue the lot tile shows a vehicle and a dollar amount, and never "No auction is live".
for (const address of ['/?view=review', '/?view=review&for=ai']) {
  test(`${address} shows a live lot with a bid on the seeded catalogue`, async ({ page }) => {
    await page.setViewportSize({ width: 1568, height: 772 });
    await freshRoute(page);
    await openTheYard(page, address);
    await expect(page.getByTestId('review-path-lot-name')).toHaveText(/\S/);
    await expect(page.getByTestId('review-path-bid')).toHaveText(/^CA\$\d{1,3}(,\d{3})*$/);
    await expect(page.getByTestId('review-path-closes')).toHaveText(/^\d+:\d{2}$/);
    await expect(page.getByText('No auction is live', { exact: false })).toHaveCount(0);
  });
}
// 10. Voices and posts are the API's: each voice's words are its entry's pull and it opens that person's card; each
// post card's title is the read's, opens its post in place and reports blog, as All posts does.
test('every voice is the answer’s own words and opens its card, and the blog row opens its posts and reports blog', async ({
  page,
}) => {
  const actions: string[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { action?: string };
    if (body.action) actions.push(body.action);
    await route.fulfill({ status: 204 });
  });
  const answer = (await (await page.request.get('/api/recommendations')).json()) as {
    recommendations: { name: string; pull: string }[];
  };
  await page.setViewportSize({ width: 1568, height: 772 });
  await freshRoute(page);
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=review');
  const names = [
    ...VOICES.quality,
    ...VOICES.gate,
    ...VOICES.room,
    ...VOICES.pair,
    ...VOICES.close,
  ];
  const voices = page.getByTestId('review-page').getByTestId('voice');
  await expect(voices).toHaveCount(names.length);
  for (const [index, name] of names.entries()) {
    const entry = answer.recommendations.find((each) => each.name === name)!;
    await expect(voices.nth(index)).toHaveAttribute('data-name', name);
    await expect(voices.nth(index)).toHaveAttribute('href', cardAddress(name));
    await expect(voices.nth(index).getByTestId('voice-pull')).toHaveText(`"${entry.pull}"`);
  }
  await expect(page.getByTestId('review-blog-title')).toHaveText(POSTS.map((post) => post.title));
  await page.getByTestId('review-blog-post').first().click();
  await expect(page).toHaveURL(/[?&]view=blog&post=a-made-up-newest-post/);
  await expect.poll(() => actions).toEqual(['blog']);
  await page.goBack();
  await page.getByTestId('review-blog-all').click();
  await expect(page).toHaveURL(/\?view=blog$/);
});

// 7. The dock: under 640 only, off at load, on once the first screen's buttons have left the window, off when they
// return, and not drawn while the booking sheet is open; the page leaves room for it under the last section.
for (const address of ['/?view=review', '/?view=review&for=ai']) {
  test(`${address}: the phone's dock comes on after the first screen and never covers the page`, async ({
    page,
  }) => {
    await page.route('**/api/go/book', (route) => route.fulfill({ status: 204 }));
    await page.setViewportSize({ width: 390, height: 844 });
    await freshRoute(page);
    await openTheYard(page, address);
    const dock = page.getByTestId('review-dock');
    await expect(dock).toHaveAttribute('data-on', 'false');
    await page.getByTestId('review-path').scrollIntoViewIfNeeded();
    await expect(dock).toHaveAttribute('data-on', 'true');
    await expect(dock.getByRole('link', { name: 'Book 20 minutes' })).toBeInViewport();
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    const close = await page.getByTestId('where-next').boundingBox();
    const bar = await dock.boundingBox();
    expect(close!.y + close!.height, 'the last section clears the dock').toBeLessThanOrEqual(
      bar!.y + 1
    );
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect(dock).toHaveAttribute('data-on', 'false');
    await page.getByTestId('review-book').click();
    await expect(page.getByTestId('book-sheet')).toBeVisible();
    await expect(dock).toHaveCount(0);
    // From 640 up there is no dock to see.
    await page.keyboard.press('Escape');
    await page.setViewportSize({ width: 1024, height: 768 });
    await expect(dock).toBeHidden();
  });
}

// 9. No dead end: every "Book 20 minutes" reports book, follows the counted redirect and opens the sheet, which reads
// the address and the message from the site's settings with a copy control for each and the LinkedIn link; Escape,
// its close and its backdrop close it.
test('Book 20 minutes opens the booking sheet with the address, the message, copy controls and LinkedIn', async ({
  page,
  context,
}) => {
  const actions: string[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { action?: string };
    if (body.action) actions.push(body.action);
    await route.fulfill({ status: 204 });
  });
  const book: string[] = [];
  await page.route('**/api/go/book', (route) => {
    book.push(route.request().url());
    return route.fulfill({ status: 204 });
  });
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  const answer = (await (await page.request.get('/api/links/book')).json()) as {
    to: string;
    subject: string;
  };
  await page.setViewportSize({ width: 1568, height: 772 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  await page.getByTestId('review-book').click();
  const sheet = page.getByTestId('book-sheet');
  await expect(sheet).toBeVisible();
  await expect(sheet.getByRole('heading', { name: 'Book 20 minutes' })).toBeInViewport();
  await expect(sheet.getByTestId('book-sheet-to')).toHaveText(answer.to);
  await expect(sheet.getByTestId('book-sheet-message')).toContainText(`Subject: ${answer.subject}`);
  await expect(sheet.getByRole('link', { name: 'Open my email app' })).toHaveAttribute(
    'href',
    '/api/go/book'
  );
  const copy = sheet.getByRole('button', { name: 'Copy the address' });
  await expect(copy).toBeInViewport();
  await expect(sheet.getByTestId('book-sheet-linkedin')).toBeInViewport();
  await copy.click();
  await expect(sheet.getByRole('button', { name: 'Copied' })).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(answer.to);
  expect(book.length).toBeGreaterThanOrEqual(1);
  await expect.poll(() => actions).toContain('book');
  await page.keyboard.press('Escape');
  await expect(sheet).toBeHidden();
  // The close's Book opens it too, and its own X closes it.
  await page.getByTestId('review-close').getByRole('link', { name: 'Book 20 minutes' }).click();
  await expect(sheet).toBeVisible();
  await sheet.getByRole('button', { name: 'Close' }).click();
  await expect(sheet).toBeHidden();
});

// 9. The send line hands the page's own address to the share sheet where there is one, and otherwise copies it and
// says so for two seconds; either way it reports forward, and it never opens a mailto.
test('the send line shares the route’s address, or copies it where there is no share sheet, and reports forward', async ({
  page,
  context,
}) => {
  const actions: string[] = [];
  await page.route('**/api/review/reached', async (route) => {
    const body = route.request().postDataJSON() as { action?: string };
    if (body.action) actions.push(body.action);
    await route.fulfill({ status: 204 });
  });
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.addInitScript(() => {
    if (sessionStorage.getItem('share-spec-off') === null)
      Object.defineProperty(navigator, 'share', {
        configurable: true,
        value: (data: ShareData) => {
          (window as unknown as { shared: ShareData[] }).shared ??= [];
          (window as unknown as { shared: ShareData[] }).shared.push(data);
          return Promise.resolve();
        },
      });
    else Object.defineProperty(navigator, 'share', { configurable: true, value: undefined });
  });
  await freshRoute(page);
  await openTheYard(page, '/?view=review&for=ai');
  const line = page.getByTestId('review-forward');
  await expect(line).toHaveText('Send this page to your recruiter or your team');
  await line.click();
  const shared = await page.evaluate(() => (window as unknown as { shared: ShareData[] }).shared);
  expect(shared).toHaveLength(1);
  expect(shared[0].title).toBe('Steven Stout, Lead / Staff .NET engineer');
  expect(shared[0].text).toMatch(
    /^His site is the running system\. Two minutes: https?:\/\/[^/]+\/\?view=review&for=ai$/
  );
  await expect.poll(() => actions).toContain('forward');
  // Without a share sheet the address is copied and the line says so.
  await page.evaluate(() => sessionStorage.setItem('share-spec-off', '1'));
  await page.reload();
  await page.getByTestId('review-forward').click();
  await expect(page.getByTestId('review-forward')).toHaveText('Link copied');
  expect(await page.evaluate(() => navigator.clipboard.readText())).toMatch(
    /\/\?view=review&for=ai$/
  );
  await expect(page.getByTestId('review-forward')).toHaveText(
    'Send this page to your recruiter or your team',
    { timeout: 5_000 }
  );
});

// No post, or a read that fails: the blog row is not drawn and the gap closes.
test('the blog row is not drawn when the blog has no post', async ({ page }) => {
  await answerBlog(page, []);
  await openTheYard(page, '/?view=review');
  await expect(page.getByTestId('review-close')).toBeVisible();
  await expect(page.getByTestId('review-blog')).toHaveCount(0);
});
// #endregion the-look
