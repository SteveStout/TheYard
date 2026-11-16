import { expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { openTheYard } from './app';

// #region where-next
/**
 * The "Where next" footer (ladder v7, page flow): at most two links at the foot of each public page, the same list
 * data/hooks.json holds, on the pages React draws and on the two the API draws. The landing's first link opens the
 * post it names, and the contact line is the recorded redirect, never an address on the page.
 */
type NextLink = { href: string };
const NEXT = (
  JSON.parse(readFileSync('data/hooks.json', 'utf8')) as {
    next: { page: string; links: NextLink[] }[];
  }
).next;
const nextFor = (page: string) => NEXT.find((entry) => entry.page === page)?.links ?? [];

const PAGES: [string, string][] = [
  ['landing', '/'],
  ['review', '/?view=review'],
  ['quality', '/?doc=quality'],
  ['about', '/about'],
  ['resume', '/resume?src=email'],
];

for (const [page, address] of PAGES) {
  test(`${address} ends with its Where next links`, async ({ page: browser }) => {
    if (address === '/about' || address.startsWith('/resume')) await browser.goto(address);
    else await openTheYard(browser, address);
    const footer = browser.locator(`[data-testid="where-next"][data-next="${page}"]`);
    await expect(footer).toBeAttached({ timeout: 30_000 });
    const hrefs = await footer
      .getByRole('link')
      .evaluateAll((links) => links.map((link) => link.getAttribute('href')));
    expect(hrefs).toEqual(nextFor(page).map((link) => link.href));
  });
}

test('the contact line answers with the email redirect, not an address on the page', async ({
  request,
}) => {
  const answer = await request.get('/api/go/email', { maxRedirects: 0 });
  expect(answer.status()).toBe(302);
  expect(answer.headers()['location']).toMatch(/^mailto:/);
});

test('a post ends with its Where next links', async ({ page }) => {
  // The local stack has no posts of its own, so the blog answers with one made up the way blog.spec.ts makes them.
  const post = {
    slug: 'a-made-up-post',
    title: 'A made-up post',
    summary: 'Two lines about a made-up post.',
    body: 'A made-up post\n\nA line of the post.',
    published_at: new Date().toISOString(),
    linkedin_url: 'https://www.linkedin.com/posts/a-made-up-post',
    code_url: 'https://github.com/SteveStout/TheYard',
    images: [],
    tags: ['dotnet'],
    reads: 1,
    reads7d: 1,
  };
  await page.route('**/api/blog/posts', (route) => route.fulfill({ json: { posts: [post] } }));
  await openTheYard(page, `/?view=blog&post=${post.slug}`);
  const footer = page.locator('[data-testid="where-next"][data-next="post"]');
  await expect(footer).toBeAttached({ timeout: 30_000 });
  const hrefs = await footer
    .getByRole('link')
    .evaluateAll((links) => links.map((link) => link.getAttribute('href')));
  expect(hrefs).toEqual(nextFor('post').map((link) => link.href));
});
// #endregion where-next
