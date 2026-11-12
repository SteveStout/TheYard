import { expect, test, type Page } from '@playwright/test';
import { REVIEW_STOPS } from '../../src/lib/reviewStops';
import { CURRENT_KEY, REACHED_KEY } from '../../src/lib/reviewProgress';
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
  await expect(page.getByTestId('view-announcement')).toHaveText('Check my work in ten minutes');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Check my work in ten minutes' })
  ).toBeVisible();
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
    ['Email Steven', 'mailto:stevenstout11@gmail.com'],
    ['Message me on LinkedIn', /^https:\/\/www\.linkedin\.com\/in\/stevenwstout\/?$/],
  ] as const;
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  const reach = page.getByTestId('review-reach');
  for (const [name, href] of ways)
    await expect(reach.getByRole('link', { name })).toHaveAttribute('href', href);
  await page.goto('/about');
  for (const [name, href] of ways)
    await expect(page.getByRole('link', { name })).toHaveAttribute('href', href);
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

test('at a wide desk the rail stands beside the stops with every pull quote the API answers, and at 390 it folds into the column', async ({
  page,
}) => {
  const answer = (await (await page.request.get('/api/recommendations')).json()) as {
    recommendations: { name: string }[];
  };
  await page.setViewportSize({ width: 1440, height: 900 });
  await freshRoute(page);
  await openTheYard(page, '/?view=review');
  const quotes = page.getByTestId('review-quotes');
  await expect(quotes.getByRole('listitem')).toHaveCount(answer.recommendations.length);
  const more = quotes.getByRole('link', { name: /Read the whole thing/ });
  await expect(more.first()).toHaveAttribute('href', /^\/\?view=recommendations#[a-z0-9-]+$/);
  const rail = await page.getByTestId('review-rail').boundingBox();
  const stops = await page.getByRole('list', { name: 'The seven stops' }).boundingBox();
  expect(rail!.x).toBeGreaterThan(stops!.x + stops!.width - 1);
  // The phone, opened at its own width as every phone test here is.
  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page, '/?view=review');
  await expect(quotes.getByRole('listitem')).toHaveCount(answer.recommendations.length);
  const finish = await page.getByTestId('review-finish').boundingBox();
  const folded = await quotes.boundingBox();
  expect(folded!.y).toBeGreaterThan(finish!.y);
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBeLessThanOrEqual(0);
});
