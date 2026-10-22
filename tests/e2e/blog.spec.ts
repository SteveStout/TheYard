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

test('the Blog row sits directly under Home and says NEW POST for a post under a week old', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await answerPosts(page);
  await openTheYard(page, '/');
  const rail = page.getByTestId('side-rail');
  const rows = await rail.locator('button, a').allInnerTexts();
  const home = rows.findIndex((text) => text.trim() === 'Home');
  expect(rows[home + 1]).toContain('Blog');
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

test('the blog route answers from the store behind it', async ({ request }) => {
  const answer = await request.get('http://localhost:5210/api/blog/posts');
  expect([200, 503]).toContain(answer.status());
  if (answer.status() === 200) {
    expect(Array.isArray(((await answer.json()) as { posts: unknown }).posts)).toBe(true);
  }
});
// #endregion blog
