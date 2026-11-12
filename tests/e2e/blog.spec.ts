import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';

// #region blog
/**
 * The blog (ADR: Every LinkedIn post is also a blog post): its row in the side nav directly under
 * Home with NEW POST while the newest post is under a week old, its own page with the newest post
 * in full and the older ones short, one post at its own address with Back to the list, the home
 * page's Latest posts band, and what a reader does reported as events. The posts here are made up
 * and answered by the test, so the layout is checked whatever the local store holds; the last test
 * asks the real API, so the route and the store behind it are checked too.
 */
const day = 86_400_000;
const made = (slug: string, title: string, ageDays: number, reads: number) => ({
  slug,
  title,
  summary: `Two lines about ${title.toLowerCase()}.`,
  body: `${title}\n\n${'A line of the post, long enough to wrap on a phone and fill the card. '.repeat(14)}\n#dotnet #csharp`,
  published_at: new Date(Date.now() - ageDays * day).toISOString(),
  linkedin_url: `https://www.linkedin.com/posts/${slug}`,
  code_url: 'https://github.com/SteveStout/TheYard',
  images: [1, 2, 3].map((position) => ({
    position,
    url: '/api/images/badges/steve-176.jpg',
    alt_text: `Slide ${position}`,
    width: 176,
    height: 176,
  })),
  tags: ['dotnet', 'csharp'],
  reads,
  reads7d: reads,
});
const POSTS = [
  made('a-made-up-newest-post', 'A made-up newest post', 1, 12),
  made('a-made-up-second-post', 'A made-up second post', 5, 30),
  made('a-made-up-third-post', 'A made-up third post', 9, 4),
  made('a-made-up-fourth-post', 'A made-up fourth post', 20, 2),
];

async function answerPosts(page: Page, posts = POSTS) {
  await page.route('**/api/blog/posts', (route) => route.fulfill({ json: { posts } }));
  await page.route('**/api/admin/blog', (route) =>
    route.fulfill({
      json: {
        kept: null,
        available: true,
        posts: [],
        sources: [
          { source: 'linkedin', reads: 6 },
          { source: 'direct', reads: 2 },
        ],
      },
    })
  );
}

for (const viewport of [
  { width: 1440, height: 900 },
  { width: 390, height: 844 },
]) {
  test(`the blog page draws the newest post in full and the rest short at ${viewport.width}`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    await answerPosts(page);
    await openTheYard(page, '/?view=blog');
    const blog = page.getByTestId('blog-page');
    await expect(blog.getByRole('heading', { level: 1, name: 'Blog' })).toBeVisible();
    await expect(blog.getByTestId('blog-post')).toHaveCount(1);
    await expect(blog.getByTestId('blog-post')).toContainText('A made-up newest post');
    await expect(blog.getByTestId('blog-mini')).toHaveCount(3);
    await expect(blog.getByTestId('blog-most-read')).toContainText('A made-up second post');
    await expect(blog.getByTestId('blog-sources')).toContainText('LinkedIn');
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways).toBeLessThanOrEqual(0);
  });
}

test('the Blog row sits third, under Home and the review route, and says NEW POST for a post under a week old', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await answerPosts(page);
  await openTheYard(page, '/');
  const rail = page.getByTestId('side-rail');
  const rows = await rail.locator('button, a').allInnerTexts();
  const home = rows.findIndex((text) => text.trim() === 'Home');
  // Third since the review route took the row under Home (ADR: Ten minutes, seven stops).
  expect(rows[home + 1]).toBe('Check my work');
  expect(rows[home + 2]).toContain('Blog');
  await expect(rail.getByTestId('rail-new-post')).toHaveText('NEW POST');
  await rail.getByRole('button', { name: /Blog/ }).click();
  await expect(page).toHaveURL(/\?view=blog$/);
  await expect(page.getByTestId('view-announcement')).toHaveText('Blog');
});

test('no NEW POST when the newest post is over a week old', async ({ page }) => {
  await answerPosts(page, [made('an-older-post', 'An older post', 8, 1)]);
  await openTheYard(page, '/');
  await expect(page.getByTestId('landing-latest')).toBeVisible();
  await expect(page.getByTestId('rail-new-post')).toHaveCount(0);
});

test('the home page shows the three newest posts, and one opens at its own address with Back home', async ({
  page,
}) => {
  await answerPosts(page);
  await openTheYard(page, '/');
  const band = page.getByTestId('landing-latest');
  await expect(band.getByRole('heading', { level: 2, name: 'Latest posts' })).toBeVisible();
  await expect(band.getByTestId('blog-mini')).toHaveCount(3);
  await band
    .getByTestId('blog-mini')
    .nth(1)
    .getByRole('button', { name: /Read the post/ })
    .click();
  await expect(page).toHaveURL(/\?view=blog&post=a-made-up-second-post$/);
  await expect(page.getByTestId('blog-post')).toContainText('A made-up second post');
  await page.goBack();
  await expect(page.getByTestId('landing')).toBeVisible();
});

test('a read and what the reader does are reported, see more once', async ({ page }) => {
  await answerPosts(page);
  const reads: unknown[] = [];
  const events: { type: string; value: string }[] = [];
  await page.route('**/api/blog/reads', async (route) => {
    reads.push(route.request().postDataJSON());
    await route.fulfill({ json: { source: 'direct' } });
  });
  await page.route('**/api/blog/events', async (route) => {
    events.push(route.request().postDataJSON() as { type: string; value: string });
    await route.fulfill({ status: 204 });
  });
  await openTheYard(page, '/?view=blog&post=a-made-up-third-post');
  await expect(page.getByTestId('blog-post')).toContainText('A made-up third post');
  await expect.poll(() => reads.length).toBe(1);
  await page.getByRole('button', { name: '…see more' }).click();
  await expect(page.getByRole('button', { name: '…see more' })).toHaveCount(0);
  await expect.poll(() => events.filter((e) => e.type === 'more').length).toBe(1);
  await page.getByRole('button', { name: 'All posts' }).click();
  await expect(page).toHaveURL(/\?view=blog$/);
});

// #region blog-holds-still
/**
 * The blog holds still on a phone while its two reads arrive (ADR-113, the addendum of @VER@): the posts and the
 * sources box are each held back a second and a half, and the page and a post at 390 by 844 move no more than the
 * 0.1 of layout shift Google counts as good. Both reads are held on every run, so a page that moves fails every time.
 */
for (const address of ['/?view=blog', '/?view=blog&post=a-made-up-second-post']) {
  test(`${address} holds still at 390 by 844 while its reads arrive`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    const held = (json: unknown) => async (route: import('@playwright/test').Route) => {
      await new Promise((resolve) => setTimeout(resolve, 1_500));
      await route.fulfill({ json });
    };
    await page.route('**/api/blog/posts', held({ posts: POSTS }));
    await page.route(
      '**/api/admin/blog',
      held({
        kept: null,
        available: true,
        posts: [],
        sources: [
          { source: 'linkedin', reads: 6 },
          { source: 'direct', reads: 2 },
        ],
      })
    );
    await page.addInitScript(() => {
      (window as unknown as { shift: number }).shift = 0;
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries() as unknown as {
          value: number;
          hadRecentInput: boolean;
        }[]) {
          if (!entry.hadRecentInput) (window as unknown as { shift: number }).shift += entry.value;
        }
      }).observe({ type: 'layout-shift', buffered: true });
    });

    await openTheYard(page, address);
    await expect(page.getByTestId('blog-list').locator('article').first()).toBeVisible({
      timeout: 10_000,
    });
    await expect(page.getByTestId('blog-sources').locator('dl')).toBeVisible();
    await page.waitForTimeout(500);

    expect(
      await page.evaluate(() => (window as unknown as { shift: number }).shift)
    ).toBeLessThanOrEqual(0.1);
  });
}
test('the sources box, read from an Admin route, has its size before the read answers', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  let answer: () => void = () => {};
  const answered = new Promise<void>((resolve) => (answer = resolve));
  await page.route('**/api/blog/posts', (route) => route.fulfill({ json: { posts: POSTS } }));
  await page.route('**/api/admin/blog', async (route) => {
    await answered;
    await route.fulfill({
      json: {
        kept: null,
        available: true,
        posts: [],
        sources: [
          { source: 'linkedin', reads: 6 },
          { source: 'ref', reads: 3 },
          { source: 'search', reads: 2 },
          { source: 'direct', reads: 1 },
        ],
      },
    });
  });
  await openTheYard(page, '/?view=blog');
  const box = page.getByTestId('blog-sources');
  await expect(box).toBeVisible();
  const before = (await box.boundingBox())!.height;
  answer();
  await expect(box.locator('dl > div')).toHaveCount(4);
  expect((await box.boundingBox())!.height).toBe(before);
});
// #endregion blog-holds-still

test('the blog route answers from the store behind it', async ({ request }) => {
  const answer = await request.get('http://localhost:5210/api/blog/posts');
  expect([200, 503]).toContain(answer.status());
  if (answer.status() === 200) {
    expect(Array.isArray(((await answer.json()) as { posts: unknown }).posts)).toBe(true);
  }
});

// #region blog-pictures
/**
 * The posts' pictures (ADR-113, the addendum on where the pictures live): a post with slides draws its
 * strip, every picture answers 200 from the site's own address with a year's immutable cache, and the
 * page is axe clean with every picture's alt text. It reads the real store, so it runs where a post has
 * pictures (both public names once the slides are in); where none has, it holds the route's 404 problem instead.
 */
test('a post with slides draws them from the site and every picture answers with a year of cache', async ({
  page,
  request,
}) => {
  const answer = await request.get('/api/blog/posts');
  const posts =
    answer.status() === 200
      ? (
          (await answer.json()) as {
            posts: { slug: string; images: { url: string; alt_text: string }[] }[];
          }
        ).posts
      : [];
  const post = posts.find((p) => p.images.length > 1);
  if (post === undefined) {
    // No post here has its slides yet (a local store): the picture route still answers a name it does not keep
    // with a 404 problem, never the page, and a made-up post's strip is held to the same look and axe.
    const missing = await request.get('/blog/images/0123456789abcdef.webp');
    expect(missing.status()).toBe(404);
    expect(missing.headers()['content-type']).toContain('application/problem+json');
    await answerPosts(page);
    await openTheYard(page, '/?view=blog&post=a-made-up-newest-post');
    await expect(page.getByTestId('blog-slides-strip')).toHaveAttribute('tabindex', '0');
    const madeUp = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();
    expect(madeUp.violations.map((v) => v.id)).toEqual([]);
    return;
  }
  for (const image of post.images) {
    expect(image.url).toMatch(/^\/blog\/images\/[0-9a-f]{16}\.(webp|png|jpg)$/);
    expect(image.alt_text.trim().length).toBeGreaterThan(0);
    const served = await request.get(image.url);
    expect(served.status()).toBe(200);
    expect(served.headers()['cache-control']).toBe('public, max-age=31536000, immutable');
  }
  await openTheYard(page, `/?view=blog&post=${post.slug}`);
  const strip = page.getByRole('region', { name: `The slides (${post.images.length})` });
  await expect(strip.locator('img')).toHaveCount(post.images.length);
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  expect(results.violations.map((v) => v.id)).toEqual([]);
});
// #endregion blog-pictures
// #endregion blog
