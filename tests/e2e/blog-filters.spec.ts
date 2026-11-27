import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';

// #region blog-filters
/**
 * The blog's filter bar (ADR: Every LinkedIn post is also a blog post, the addendum on the read model): a topic chip,
 * a keyword chip and the search box each narrow the list and write the address, Back and Forward walk the filters,
 * a search asks the API once per word typed, an empty result says what was asked and clears, a page more of posts
 * reads on, and the list on screen stays, dimmed, while the next one is read. The posts are made up and answered by
 * the test (blogFake.ts); what a word widens to and how posts rank is the API's, held by its own tests.
 */
const POSTS = [
  made('microsoft-ignite-all-in-on-ai', 'Microsoft Ignite: all in on AI', 1, {
    topics: ['ai', 'dotnet', 'azure'],
    keywords: ['microsoft', 'copilot'],
    reads: 9,
  }),
  made('onion-for-beginners', 'Onion architecture for beginners', 2, {
    topics: ['architecture', 'dotnet'],
    keywords: ['cqrs'],
    reads: 3,
  }),
  made('scammers-read-the-job-market', 'Scammers read the job market too', 3, {
    topics: ['security', 'job-search'],
    reads: 20,
  }),
  made('i-plan-with-claude', 'I plan with Claude', 4, {
    topics: ['ai'],
    keywords: ['claude'],
    reads: 5,
  }),
  ...Array.from({ length: 8 }, (_, n) =>
    made(`a-career-note-${n + 1}`, `A career note ${n + 1}`, 10 + n, { topics: ['career'] })
  ),
];

const slugs = (page: Page) =>
  page
    .getByTestId('blog-list')
    .locator('[data-testid="blog-post"] h2, [data-testid="blog-mini"] h3')
    .allInnerTexts();

/** The keywords fold behind one pill (ADR-142): a reader opens it before pressing a keyword chip. */
async function showKeywords(page: Page) {
  const toggle = page.getByTestId('blog-keywords-toggle');
  if ((await toggle.getAttribute('aria-expanded')) === 'false') await toggle.click();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
}

test('a topic chip narrows the list, writes the address, and Back and Forward walk it', async ({
  page,
}) => {
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts');
  await page
    .getByTestId('blog-filters')
    .getByRole('button', { name: /^Security/ })
    .click();
  await expect(page).toHaveURL(/\?view=blog&topic=security$/);
  await expect(page.getByTestId('blog-count')).toHaveText('Showing 1 of 12 posts');
  await expect.poll(() => slugs(page)).toEqual(['Scammers read the job market too']);
  await page.goBack();
  await expect(page).toHaveURL(/\?view=blog$/);
  await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts');
  await page.goForward();
  await expect(
    page.getByTestId('blog-filters').getByRole('button', { name: /^Security/ })
  ).toHaveAttribute('aria-pressed', 'true');
});

test('a keyword chip narrows the list to the posts that really talk about it', async ({ page }) => {
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog');
  await showKeywords(page);
  await page
    .getByTestId('blog-filters')
    .getByRole('button', { name: /^Microsoft Copilot/ })
    .click();
  await expect(page).toHaveURL(/keyword=copilot/);
  // The list is redrawn as a transition after the address moves, so it is read until it settles.
  await expect.poll(() => slugs(page)).toEqual(['Microsoft Ignite: all in on AI']);
});

for (const [typed, found] of [
  ['copilot', ['Microsoft Ignite: all in on AI']],
  ['ms', ['Microsoft Ignite: all in on AI']],
  ['scams', ['Scammers read the job market too']],
  ['llm', ['Microsoft Ignite: all in on AI', 'I plan with Claude']],
] as const) {
  test(`typing "${typed}" asks once, writes the address, and says where each post matched`, async ({
    page,
  }) => {
    const asked = await answerBlog(page, POSTS);
    await openTheYard(page, '/?view=blog');
    await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts');
    const before = asked.length;
    await page.getByTestId('blog-search').pressSequentially(typed, { delay: 40 });
    await expect(page).toHaveURL(new RegExp(`q=${typed}$`));
    await expect(page.getByTestId('blog-count')).toHaveText(`Showing ${found.length} of 12 posts`);
    await expect.poll(() => slugs(page)).toEqual([...found]);
    expect(asked.slice(before)).toEqual([`?q=${typed}`]);
    await expect(page.getByTestId('blog-matched').first()).toContainText('Matched on');
  });
}

test('a search marks the widened words in the titles', async ({ page }) => {
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog&q=ms');
  await expect(page.getByTestId('blog-list').locator('mark').first()).toHaveText('Microsoft');
});

test('an empty result says what was asked, and Clear brings every post back', async ({ page }) => {
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog&q=kubernetes&topic=security');
  await expect(page.getByTestId('blog-empty')).toContainText(
    'No post matches "kubernetes" in Security.'
  );
  await page.getByTestId('blog-empty').getByRole('button', { name: 'Clear the filters' }).click();
  await expect(page).toHaveURL(/\?view=blog$/);
  await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts');
  await expect(page.getByTestId('blog-search')).toHaveValue('');
});

test('More posts reads the next page and adds it under the first', async ({ page }) => {
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-mini')).toHaveCount(9);
  await page.getByTestId('blog-more').click();
  await expect(page.getByTestId('blog-mini')).toHaveCount(11);
  await expect(page.getByTestId('blog-more')).toHaveCount(0);
});

test('Back from a post opened past More posts comes home to every page it had read', async ({
  page,
}) => {
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-mini')).toHaveCount(9);
  await page.getByTestId('blog-more').click();
  await expect(page.getByTestId('blog-mini')).toHaveCount(11);
  await page
    .getByTestId('blog-mini')
    .last()
    .getByRole('button', { name: /Read the post/ })
    .click();
  await expect(page.getByTestId('blog-post')).toBeVisible();
  await page.goBack();
  await expect(page.getByTestId('blog-count')).toBeVisible();
  await expect(page.getByTestId('blog-mini')).toHaveCount(11);
  await expect(page.getByTestId('blog-more')).toHaveCount(0);
});

test('the list on screen stays, dimmed, while the next one is read', async ({ page }) => {
  await answerBlog(page, POSTS, 1_200);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts', {
    timeout: 10_000,
  });
  await page.getByTestId('blog-sort').selectOption('read');
  await expect(page.getByTestId('blog-list')).toHaveAttribute('aria-busy', 'true');
  await expect(page.getByTestId('blog-mini').first()).toBeVisible();
  await expect(page.getByTestId('blog-list')).toHaveAttribute('aria-busy', 'false', {
    timeout: 10_000,
  });
  await expect.poll(async () => (await slugs(page))[0]).toBe('Scammers read the job market too');
  await expect(page).toHaveURL(/sort=read/);
});

test('at phone width the bar fits, nothing scrolls sideways, and the first post starts on the first screen', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await answerBlog(page, POSTS);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-post')).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    )
  ).toBeLessThanOrEqual(0);
  const top = (await page.getByTestId('blog-post').boundingBox())!.y;
  expect(top).toBeLessThan(844);
});
// #region the-exploratory-pass
// Found by the exploratory pass of findable1120 (every chip by click and by a typed address in three cases, with Back,
// Forward and Reload, at 1440 and 390): an address typed with a topic or keyword in capitals dropped the filter and
// showed every post with no chip pressed. A topic or keyword is matched without regard to case, in the page and in the
// API, so a link someone types or a tool rewrites still opens the list it names.
for (const width of [1440, 390]) {
  for (const [part, typed, chip, found] of [
    ['topic', 'AI', /^AI/, ['Microsoft Ignite: all in on AI', 'I plan with Claude']],
    ['topic', 'Job-Search', /^Job search/, ['Scammers read the job market too']],
    ['keyword', 'CQRS', /^CQRS/, ['Onion architecture for beginners']],
    ['keyword', 'cLaUdE', /^Claude/, ['I plan with Claude']],
  ] as const) {
    test(`a typed ${part}=${typed} narrows the list as its chip does, and still does after Reload, at ${width}`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
      await answerBlog(page, POSTS);
      await openTheYard(page, `/?view=blog&${part}=${typed}`);
      await expect(page.getByTestId('blog-count')).toHaveText(
        `Showing ${found.length} of 12 posts`
      );
      await expect(
        page.getByTestId('blog-filters').getByRole('button', { name: chip })
      ).toHaveAttribute('aria-pressed', 'true');
      await expect.poll(() => slugs(page)).toEqual([...found]);
      await page.reload();
      await expect(page.getByTestId('blog-count')).toHaveText(
        `Showing ${found.length} of 12 posts`
      );
      await expect.poll(() => slugs(page)).toEqual([...found]);
    });
  }

  test(`every chip by click survives Back, Forward and Reload at ${width}`, async ({ page }) => {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await answerBlog(page, POSTS);
    await openTheYard(page, '/?view=blog');
    for (const [chip, address, shown] of [
      [/^Architecture/, /\?view=blog&topic=architecture$/, 1],
      [/^Career/, /\?view=blog&topic=career$/, 8],
      [/^Microsoft Copilot/, /\?view=blog&keyword=copilot$/, 1],
    ] as const) {
      const bar = page.getByTestId('blog-filters');
      if (String(address).includes('keyword')) await showKeywords(page);
      await bar.getByRole('button', { name: chip }).click();
      await expect(page).toHaveURL(address);
      await expect(page.getByTestId('blog-count')).toHaveText(`Showing ${shown} of 12 posts`);
      await page.goBack();
      await expect(page).toHaveURL(/\?view=blog$/);
      await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts');
      await page.goForward();
      await expect(page).toHaveURL(address);
      await expect(page.getByTestId('blog-count')).toHaveText(`Showing ${shown} of 12 posts`);
      await page.reload();
      await expect(page.getByTestId('blog-count')).toHaveText(`Showing ${shown} of 12 posts`);
      await expect(bar.getByRole('button', { name: chip })).toHaveAttribute('aria-pressed', 'true');
      await bar.getByRole('button', { name: chip }).click();
      await expect(page).toHaveURL(/\?view=blog$/);
      await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts');
    }
  });

  test(`a post opened from a filtered list comes Back to that filter at ${width}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await answerBlog(page, POSTS);
    await openTheYard(page, '/?view=blog&topic=CAREER');
    await expect(page.getByTestId('blog-count')).toHaveText('Showing 8 of 12 posts');
    await page
      .getByTestId('blog-mini')
      .first()
      .getByRole('button', { name: /Read the post/ })
      .click();
    await expect(page).toHaveURL(/\?view=blog&post=a-career-note-2$/);
    await expect(page.getByTestId('blog-post')).toContainText('A career note 2');
    await page.goBack();
    await expect(page.getByTestId('blog-count')).toHaveText('Showing 8 of 12 posts');
    await expect(
      page.getByTestId('blog-filters').getByRole('button', { name: /^Career/ })
    ).toHaveAttribute('aria-pressed', 'true');
  });
}
// #endregion the-exploratory-pass
// #endregion blog-filters
