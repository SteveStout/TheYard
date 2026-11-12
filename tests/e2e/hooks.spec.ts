import { expect, test, type Locator, type Page } from '@playwright/test';
import { openTheYard } from './app';
import file from '../../data/hooks.json' with { type: 'json' };

// #region hooks
/**
 * A hook on every page (ADR-132): on a phone, 390 by 844, each page a reader is sent to opens with its hook, and the
 * hook's whole box sits on the first screen with no scroll. The landing, /about, the resume door and a blog post each
 * draw their entry of data/hooks.json; the Quality page's hook is its own first chart, and the review route's is stop
 * 1, which starts on the first screen once the person panel folds below the stops. The post is made up and
 * answered by the test, with a tall first image, so the box is held at its largest.
 */
const PHONE = { width: 390, height: 844 };
const lines = (page: string) => file.hooks.find((hook) => hook.page === page)!.lines;

const POST = {
  slug: 'a-made-up-post-with-a-hook',
  title: 'A made-up post whose title is long enough to wrap onto a second line on a phone',
  summary:
    'Two sentences of summary, the length a real post carries. The second one is here so the box is as tall as a real one.',
  body: `A made-up post\n\n${'A line of the post, long enough to wrap on a phone and fill the card. '.repeat(14)}`,
  published_at: new Date(Date.now() - 86_400_000).toISOString(),
  linkedin_url: 'https://www.linkedin.com/posts/a-made-up-post-with-a-hook',
  code_url: null,
  images: [1, 2].map((position) => ({
    position,
    url: '/api/images/badges/steve-176.jpg',
    alt_text: `Slide ${position}`,
    width: 1080,
    height: 1350,
  })),
  tags: ['dotnet'],
  reads: 3,
  reads7d: 3,
};

async function onTheFirstScreen(box: Locator, name: string) {
  await expect(box, name).toBeVisible({ timeout: 30_000 });
  const at = (await box.boundingBox())!;
  expect(at.y, `${name} starts on the page`).toBeGreaterThanOrEqual(0);
  expect(at.y + at.height, `${name} ends inside the first 844 px`).toBeLessThanOrEqual(
    PHONE.height
  );
}

async function atPhoneSize(page: Page) {
  await page.setViewportSize(PHONE);
}

test.describe('at 390 by 844 each hook sits on the first screen', () => {
  test('the landing: the quality sentence, the Quality page first chart and its link', async ({
    page,
  }) => {
    await atPhoneSize(page);
    await openTheYard(page, '/');
    const hook = page.locator('[data-hook="landing"]');
    await expect(hook.getByRole('heading', { level: 1 })).toHaveText(lines('landing')[0]);
    await expect(hook.getByTestId('quality-trend')).toBeVisible();
    await expect(hook.getByRole('link')).toHaveAttribute('href', '/?doc=quality');
    await onTheFirstScreen(hook, 'the landing hook');
    // The live readouts stay directly below it.
    const band = (await page.getByTestId('pulse-band').boundingBox())!;
    const box = (await hook.boundingBox())!;
    expect(band.y).toBeGreaterThan(box.y + box.height - 1);
    expect(band.y - (box.y + box.height)).toBeLessThan(48);
  });

  test('/about: the resume two lines', async ({ page }) => {
    await atPhoneSize(page);
    await page.goto('/about');
    const hook = page.locator('[data-hook="about"]');
    for (const line of lines('about')) await expect(hook).toContainText(line);
    await onTheFirstScreen(hook, 'the About hook');
  });

  test('/resume: the quality sentence and the recommendations', async ({ page }) => {
    await atPhoneSize(page);
    await page.goto('/resume?src=email');
    const hook = page.locator('[data-hook="resume"]');
    await expect(hook).toContainText(lines('resume')[0]);
    await expect(hook.getByRole('link')).toHaveAttribute('href', '/recommendations');
    await onTheFirstScreen(hook, 'the resume door hook');
  });

  test('a blog post: its title, its summary and its first image', async ({ page }) => {
    await atPhoneSize(page);
    await page.route('**/api/blog/posts', (route) => route.fulfill({ json: { posts: [POST] } }));
    await openTheYard(page, `/?view=blog&post=${POST.slug}`);
    const hook = page.locator('[data-hook="post"]');
    await expect(hook).toContainText(POST.title);
    await expect(hook).toContainText(POST.summary);
    await expect(hook.locator('img')).toHaveAttribute('alt', 'Slide 1');
    await onTheFirstScreen(hook, 'the post hook');
  });

  test('/?doc=quality: the page own first chart', async ({ page }) => {
    await atPhoneSize(page);
    await openTheYard(page, '/?doc=quality');
    const chart = page.locator('dialog[open]').getByTestId('quality-trend').first();
    await onTheFirstScreen(chart, 'the Quality page first chart');
  });

  test('the review route: stop 1, its heading and what it shows, before the person panel', async ({
    page,
  }) => {
    await atPhoneSize(page);
    await openTheYard(page, '/?view=review');
    const first = page.getByRole('list', { name: 'The seven stops' }).getByRole('listitem').first();
    await onTheFirstScreen(first.getByRole('heading', { level: 2 }), 'stop 1 heading');
    await onTheFirstScreen(first.locator('p').nth(1), 'what stop 1 shows');
  });
});
// #endregion hooks
