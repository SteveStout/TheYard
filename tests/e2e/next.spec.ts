import { expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';

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

// The home page, the review route and the Quality page end on their close, with no Where next under it.
for (const address of ['/', '/?view=review', '/?doc=quality']) {
  test(`${address} ends on its close, with no Where next`, async ({ page }) => {
    await openTheYard(page, address);
    await expect(page.locator('[data-mock="close"]').first()).toBeAttached({ timeout: 30_000 });
    await expect(page.locator('[data-testid="where-next"]')).toHaveCount(0);
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
  const post = made('a-made-up-post', 'A made-up post', 0, { slides: 0 });
  await answerBlog(page, [post]);
  await openTheYard(page, `/?view=blog&post=${post.slug}`);
  const footer = page.locator('[data-testid="where-next"][data-next="post"]');
  await expect(footer).toBeAttached({ timeout: 30_000 });
  const hrefs = await footer
    .getByRole('link')
    .evaluateAll((links) => links.map((link) => link.getAttribute('href')));
  expect(hrefs).toEqual(nextFor('post').map((link) => link.href));
});
test('a tagged visit carries its code through the footers, and each page it lands on reports it', async ({
  page,
  request,
}) => {
  // Ladder v7b, the proof walk (R7): the stack page with a code, its footer to the post, then a sent About link,
  // then the email line. That the API keeps each open under its path is LinkEndpointStatusTests' and
  // LinkHandlerTests'; this holds that every step sends the code and is answered.
  const code = `zz${Date.now().toString(36).slice(-4)}`;
  await answerBlog(page, []);
  await openTheYard(page, `/?view=stack&r=${code}`);
  const toPost = page
    .locator('[data-next="landing"]')
    .getByRole('link', { name: 'Quality on both ends' });
  await expect(toPost).toHaveAttribute('href', new RegExp(`r=${code}`));
  const blogReport = page.waitForResponse((response) =>
    response.url().includes(`/api/links/open?r=${code}&path=/blog`)
  );
  await toPost.click();
  expect((await blogReport).status()).toBe(204);
  // A sent About link already in an inbox, /about?r=<code>, is a door: it answers 302 to the author page with the
  // code kept, and the page it lands on reports the open once, under /about (ADR: Tagged links, the About link).
  const door = await request.get(`/about?r=${code}`, { maxRedirects: 0 });
  expect(door.status()).toBe(302);
  expect(door.headers()['location']).toBe(`/?doc=author&r=${code}`);
  const aboutReport = page.waitForResponse((response) =>
    response.url().includes(`/api/links/open?r=${code}&path=/about`)
  );
  await page.goto(`/about?r=${code}`);
  expect((await aboutReport).status()).toBe(204);
  expect((await request.get(`/api/go/email?r=${code}`, { maxRedirects: 0 })).status()).toBe(302);
});
// #endregion where-next

// #region one-close
/**
 * One path (ADR: The sales pages set the site's look, the addendum on the one close): a blog post, About and the
 * recommendations page each end on the site's one closing row, the calibrated question and its three moves, and their
 * Where next leads back to the review page; a post and the recommendations page open the review page's booking sheet.
 */
const QUESTION = 'What has to work, where is it today, and what does success look like?';
const MOVES = ['Book 20 minutes', 'Resume (PDF)', 'Message me on LinkedIn'];
const SPOKES: { page: string; address: string; links: string[]; closeFirst: boolean }[] = [
  {
    page: 'post',
    address: '/?view=blog&post=a-made-up-post',
    links: ['/?view=review', '/?view=about'],
    closeFirst: true,
  },
  {
    page: 'about',
    address: '/about',
    links: ['/?view=review', '/?view=recommendations'],
    closeFirst: true,
  },
  {
    page: 'recommendations',
    address: '/?view=recommendations',
    links: ['/?view=review', '/?view=about'],
    closeFirst: false,
  },
];

for (const [width, height] of [
  [1440, 900],
  [390, 844],
] as const) {
  for (const spoke of SPOKES) {
    test(`at ${width} ${spoke.page} ends on the closing question, its three moves and the way back to the review page`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height });
      if (spoke.page === 'about') await page.goto(spoke.address);
      else {
        await answerBlog(page, [made('a-made-up-post', 'A made-up post', 0, { slides: 0 })]);
        await openTheYard(page, spoke.address);
      }
      const close = page.locator('[data-mock="close"]');
      await expect(close.locator('[data-mock="close-question"]')).toHaveText(QUESTION, {
        timeout: 30_000,
      });
      const moves = close.locator('[data-mock="close-moves"] > a');
      await expect(moves).toHaveCount(3);
      expect(
        (await moves.allInnerTexts()).map((text) => text.replace(/^PDF\s*/, '').trim())
      ).toEqual(MOVES);
      await expect(moves.nth(0)).toHaveAttribute('href', '/api/go/book');
      await expect(moves.nth(1)).toHaveAttribute('href', '/resume?pdf=1');
      await expect(moves.nth(1)).toHaveClass(/op-pill-resume/);
      const next = page.locator('[data-mock="where-next"]');
      const hrefs = await next
        .locator('[data-mock="next-link"]')
        .evaluateAll((links) => links.map((link) => link.getAttribute('href')));
      expect(hrefs).toEqual(spoke.links);
      const closeTop = (await close.boundingBox())!.y;
      const nextTop = (await next.boundingBox())!.y;
      expect(closeTop < nextTop).toBe(spoke.closeFirst);
      // Every move is a thumb tall on a phone.
      if (width === 390)
        for (const move of await moves.all())
          expect((await move.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    });
  }
}

test('Book 20 minutes at a post’s foot and on the recommendations page opens the booking sheet', async ({
  page,
}) => {
  await page.route('**/api/go/book', (route) => route.fulfill({ status: 204 }));
  await answerBlog(page, [made('a-made-up-post', 'A made-up post', 0, { slides: 0 })]);
  for (const address of ['/?view=blog&post=a-made-up-post', '/?view=recommendations']) {
    await openTheYard(page, address);
    await page
      .locator('[data-mock="close-moves"]')
      .getByRole('link', { name: 'Book 20 minutes' })
      .click();
    const sheet = page.getByTestId('book-sheet');
    await expect(sheet).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(sheet).toBeHidden();
  }
});
// #endregion one-close
