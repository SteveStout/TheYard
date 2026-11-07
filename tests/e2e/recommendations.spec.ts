import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { fadedContent, quietWordsOnTheBareGround } from './glass';

// #region recommendations
/**
 * The recommendations page (ADR: The recommendations have a page): at a desk's width and a phone's it shows every
 * name the API answers, each linked to the profile the API gives, grouped as the API groups them, with no sideways
 * scroll and no WCAG 2.1 AA violation; a tagged link reports under its own path; its short door keeps the code.
 * Every count and name is read from /api/recommendations in the test, never typed.
 */
type Answer = {
  count: number;
  recommendations: { name: string; profile: string }[];
  groups: { key: string; names: string[] }[];
};

async function answer(page: Page): Promise<Answer> {
  return (await (await page.request.get('/api/recommendations')).json()) as Answer;
}

for (const width of [1280, 390]) {
  test(`at ${width} the page shows every recommendation the API answers, grouped as it groups them`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    const api = await answer(page);
    await openTheYard(page, '/?view=recommendations');
    const view = page.getByTestId('recommendations-page');
    await expect(view).toHaveAttribute('data-state', 'ready');
    await expect(view.getByRole('heading', { level: 1 })).toHaveText(
      `${api.count} people who worked with Steven`
    );
    const cards = view.getByTestId('recommendation-card');
    await expect(cards).toHaveCount(api.recommendations.length);
    for (const entry of api.recommendations) {
      await expect(
        view.getByRole('link', { name: entry.name, exact: true }).first()
      ).toHaveAttribute('href', entry.profile);
    }
    for (const group of api.groups) {
      await expect(view.locator(`section[aria-labelledby="group-${group.key}"]`)).toBeVisible();
    }
    await expect(
      view.getByRole('navigation', { name: 'On this page' }).getByRole('link')
    ).toHaveCount(api.recommendations.length);
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways).toBeLessThanOrEqual(0);
    expect(await fadedContent(page)).toEqual([]);
    expect(await quietWordsOnTheBareGround(page)).toEqual([]);
    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();
    expect(results.violations.map((v) => v.id)).toEqual([]);
  });
}

test('a tagged link that opens the page reports its open under /recommendations', async ({
  page,
}) => {
  const reports: string[] = [];
  await page.route('**/api/links/open**', async (route) => {
    reports.push(route.request().url());
    await route.fulfill({ status: 204 });
  });
  await openTheYard(page, '/?view=recommendations&r=zzrec1');
  await expect.poll(() => reports.length).toBe(1);
  expect(new URL(reports[0]).searchParams.get('path')).toBe('/recommendations');
  await expect(page).toHaveURL(/\?view=recommendations/);
});

test('the short door /recommendations is a permanent redirect that keeps the code', async ({
  request,
}) => {
  const response = await request.get('/recommendations?r=zzrec1', { maxRedirects: 0 });
  expect(response.status()).toBe(301);
  expect(response.headers()['location']).toBe('/?view=recommendations&r=zzrec1');
});
// #endregion recommendations
