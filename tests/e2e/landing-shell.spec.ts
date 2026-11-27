import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { answerBlog, made, postAnswer } from './blogFake';

/**
 * The six landing views share the review route's shell (ADR-142, the addendum on the landing views): the rail opens
 * closed, the band is the live sentence and the box is the review route's. These hold what a returning visitor keeps,
 * what the About page puts on a phone's first screen, the blog's bylines, and a post whose code has one long line.
 * The blog's reads are the test's own stand-ins (blogFake.ts); nothing here reads or writes a real store (RULE 79).
 */

const POSTS = [
  made('newest-post', 'The newest post', 1, { topics: ['dotnet'] }),
  made('older-post', 'An older post', 3, { topics: ['azure'] }),
];

/** A fresh visitor: no rail choice kept. */
const fresh = (page: Page) => page.addInitScript(() => localStorage.removeItem('theyard.rail'));

test('every landing view opens with the rail closed and the live band, and the inventory keeps the store toggle', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await fresh(page);
  await answerBlog(page, POSTS);
  for (const path of [
    '/',
    '/?view=review',
    '/?view=about',
    '/?view=blog',
    '/?view=recommendations',
  ]) {
    await openTheYard(page, path);
    await expect(page.locator('[data-rail]').first(), path).toHaveAttribute(
      'data-rail',
      'collapsed'
    );
    await expect(page.getByTestId('store-bar'), path).toHaveAttribute('data-state', 'review');
    await expect(page.locator('main'), path).toHaveAttribute('data-landing', '');
  }
  await openTheYard(page, '/?view=inventory');
  await expect(page.locator('main')).not.toHaveAttribute('data-landing', '');
  await expect(page.getByTestId('store-bar')).not.toHaveAttribute('data-state', 'review');
});

test('a returning visitor who opened the rail keeps it open on a landing view', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  // A fresh context keeps no rail choice; no init script clears it, so the choice made below survives the next visit.
  await openTheYard(page, '/?view=about');
  const rail = page.getByTestId('side-rail');
  await expect(rail).toHaveAttribute('data-collapsed', 'true');
  await rail.getByRole('button', { name: 'Expand the sidebar' }).click();
  await expect(rail).toHaveAttribute('data-collapsed', 'false');
  // The choice is the visitor's own (useRail, ADR-013): kept between visits, on every landing view.
  await page.goto('/?view=recommendations');
  await expect(page.getByTestId('side-rail')).toHaveAttribute('data-collapsed', 'false');
});

test('Admin is reachable from the closed rail on a landing view', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await fresh(page);
  await openTheYard(page, '/?view=about');
  const rail = page.getByTestId('side-rail');
  await expect(rail).toHaveAttribute('data-collapsed', 'true');
  await rail
    .getByRole('button', { name: /^Admin/ })
    .first()
    .click();
  await expect(page).toHaveURL(/view=admin/);
});

test("About's first screen on a phone shows his photo, straight after the title", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page, '/?view=about');
  const photo = page.locator('[data-mock="hero-photo"]');
  await expect(photo).toBeVisible();
  const title = (await page.locator('main h1').boundingBox())!;
  const box = (await photo.boundingBox())!;
  expect(box.y).toBeGreaterThan(title.y);
  expect(box.y).toBeLessThan(844);
});

test("the blog's first post byline is his photo, never the initials", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog');
  const byline = page.getByTestId('blog-post').first().locator('header img');
  await expect(byline).toHaveAttribute('src', /steve/);
  await expect(page.getByTestId('blog-post').first().locator('header')).not.toContainText(/^SS/);
});

for (const width of [390, 1440]) {
  test(`a post with one long code line scrolls inside its own box, never the page, at ${width}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    await answerBlog(page, POSTS);
    // The post's body as the API draws a fenced block on save: one unbroken line far wider than any phone.
    const line = `az webapp config container set --name app-${'x'.repeat(240)} --docker-custom-image-name image`;
    await page.route(/\/api\/blog\/posts\/newest-post(\?.*)?$/, async (route) => {
      const post = postAnswer(POSTS, 'newest-post')!;
      await route.fulfill({
        json: {
          ...post,
          body_html: `<p>The setting.</p>\n<pre><code>${line}</code></pre>\n<p>After it.</p>\n`,
        },
      });
    });
    await openTheYard(page, '/?view=blog&post=newest-post');
    await expect(page.locator('main h1')).toHaveText('The newest post');
    await expect(page.locator('main h1')).toHaveCount(1);
    const pre = page.getByTestId('blog-post-body').locator('pre');
    await expect(pre).toBeVisible();
    const sizes = await page.evaluate(() => ({
      page: document.documentElement.scrollWidth,
      window: window.innerWidth,
    }));
    expect(sizes.page).toBeLessThanOrEqual(sizes.window);
    const own = await pre.evaluate((element) => ({
      scroll: element.scrollWidth,
      client: element.clientWidth,
      overflow: getComputedStyle(element).overflowX,
    }));
    expect(own.scroll).toBeGreaterThan(own.client);
    expect(['auto', 'scroll']).toContain(own.overflow);
  });
}
