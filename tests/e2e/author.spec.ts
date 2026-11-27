import { expect, test, type Locator, type Page } from '@playwright/test';
import { openSection, openTheYard } from './app';
import { listAnswer, made } from './blogFake';

/**
 * The parts of the page in the order a hiring manager reads them, the work woven between the personal rows and the one
 * ask last (ADR: The sidebar, the addendums on About Steven as a page and on the person beside the work; ADR-142, the
 * addendum on the landing views; src/lib/aboutOrder.ts).
 */
const ORDER = [
  'page-head',
  'lead-grid',
  'away-card',
  'proof',
  'voices',
  'voices-more',
  'coaching',
  'why-write',
  'away-strip',
  'close',
];

/**
 * The woven page at any width: three stories each inside its own lead card, the team drawing inside the coaching card,
 * seven away cards and then the strip as the away grid's only children, and all nineteen pictures.
 */
async function expectWoven(about: Page) {
  const lead = about.locator('[data-mock="lead-grid"] > [data-mock="lead-card"]');
  await expect(lead).toHaveCount(3);
  await expect(about.locator('[data-mock="lead-grid"] > *')).toHaveCount(3);
  for (const index of [0, 1, 2])
    await expect(lead.nth(index).locator('[data-mock="aside"]')).toHaveCount(1);
  await expect(about.locator('[data-mock="aside"]')).toHaveCount(3);
  await expect(about.locator('[data-mock="aside-title"]')).toHaveText([
    'The workshop',
    'The doors',
    'The lake, and no boat',
  ]);
  await expect(
    about.locator('[data-mock="coaching"] [data-mock="lead-picture"] img[src*="how-i-lead"]')
  ).toHaveCount(1);
  await expect(about.locator('[data-mock="away-card"] h3')).toHaveText([
    'St. Charles',
    'History',
    'Food',
    'Games',
    'Modding',
    'Too many house plants',
    'Freya and Vincent',
  ]);
  await expect(about.locator('[data-mock="away-label"]')).toHaveCount(7);
  await expect(about.locator('[data-mock="away-strip"]')).toHaveCount(1);
  // Nothing removed: the nineteen pictures (the hero's with them) and eight captions, and the newest post's cover.
  await expect(about.locator('[data-testid="about-page"] figure img')).toHaveCount(19);
  await expect(about.locator('[data-testid="about-page"] figcaption')).toHaveCount(8);
  await expect(about.locator('[data-mock="latest-post"]')).toHaveCount(1);
}

/**
 * The Author section (ADR: The sidebar, the addendum on About Steven as a page): one link, About Steven, the last
 * section of the sidebar in both of its shapes, and it opens the page in a tab of its own, as the recommendations and
 * the stack do. It is read on a phone first, so the phone is where its width is held.
 */
async function theAuthorPage(page: Page, menu: Locator): Promise<Page> {
  await openSection(menu, 'About Steven');
  const link = menu.getByRole('link', { name: 'About Steven', exact: true });
  await expect(link).toHaveAttribute('href', '/?view=about');
  const [opened] = await Promise.all([page.context().waitForEvent('page'), link.click()]);
  await opened.waitForLoadState();
  await expect(opened).toHaveURL(/\?view=about$/);
  await expect(opened.getByTestId('about-page')).toBeVisible();
  return opened;
}

/** The page's parts, top to bottom, by where each one stands. */
async function partsInOrder(page: Page): Promise<string[]> {
  return page.evaluate((hooks) => {
    const tops = hooks.map((hook) => ({
      hook,
      top: document.querySelector(`[data-mock="${hook}"]`)?.getBoundingClientRect().top ?? NaN,
    }));
    return tops.sort((a, b) => a.top - b.top).map((part) => part.hook);
  }, ORDER);
}

/** The newest post Why I write shows, the test's own (RULE 79): every tab this test opens reads this list. */
const answerPosts = (page: Page) =>
  page.context().route(/\/api\/blog\/posts(\?.*)?$/, (route) =>
    route.fulfill({
      json: listAnswer(
        [made('the-newest-post', 'The newest post', 1)],
        new URL(route.request().url())
      ),
    })
  );

test('About Steven opens from the sidebar and from the phone drawer, offers four ways to reach him, and fits a phone', async ({
  page,
}) => {
  await answerPosts(page);
  await page.setViewportSize({ width: 1280, height: 900 });
  await openTheYard(page);
  const wide = await theAuthorPage(page, page.getByTestId('side-rail'));
  // A page, not a window: nothing opens over it, and the parts stand in the manager's order.
  await expect(wide.locator('dialog[open]')).toHaveCount(0);
  await expect(wide.getByTestId('about-page').locator('[data-mock="eyebrow"]')).toHaveText(
    /^Steven Stout\/About\//
  );
  await expect(wide.locator('main h1')).toHaveText("Hi, I'm Steve.");
  await expect.poll(() => partsInOrder(wide)).toEqual(ORDER);
  // Six ways to reach him under his words: the review route, the gold resume and Book 20 minutes, then LinkedIn,
  // GitHub and the blog in the roles row.
  const moves = wide.locator('[data-mock="ctas"] > a, [data-mock="strip"] a');
  // Each button's own words: the PDF mark and the arrow are pictures beside them, not part of the label.
  await expect
    .poll(() =>
      moves.evaluateAll((links) =>
        links.map((link) =>
          Array.from(link.childNodes)
            .filter((node) => node.nodeType === Node.TEXT_NODE)
            .map((node) => node.textContent)
            .join('')
            .trim()
        )
      )
    )
    .toEqual([
      'Check my work in 2 minutes',
      'Resume (PDF)',
      'Book 20 minutes',
      'LinkedIn',
      'GitHub',
      'Blog',
    ]);
  await expect(moves.nth(0)).toHaveAttribute('href', '/?view=review');
  await expect(moves.nth(1)).toHaveAttribute('href', '/resume?pdf=1');
  await expect(moves.nth(2)).toHaveAttribute('href', '/api/go/book');
  await expect(moves.nth(3)).toHaveAttribute('href', 'https://www.linkedin.com/in/stevenwstout');
  await expect(moves.nth(4)).toHaveAttribute('href', 'https://github.com/SteveStout/TheYard');
  await expect(moves.nth(5)).toHaveAttribute('href', '/?view=blog');
  // The review route's three tiles, each door leaving for its case card there.
  const doors = wide.locator('[data-mock="proof"] > li a');
  await expect(doors).toHaveCount(3);
  for (const [index, card] of ['case-conway', 'case-storee', 'case-theyard'].entries())
    await expect(doors.nth(index)).toHaveAttribute('href', `/?view=review#${card}`);
  // Six voices in two runs of three, the link to every recommendation, and one close.
  await expect(wide.locator('[data-mock="voices"] [data-mock="voice"]')).toHaveCount(3);
  await expect(wide.locator('[data-mock="voices-more"] [data-mock="voice"]')).toHaveCount(3);
  await expect(
    wide.locator('[data-mock="voices"] a[href="/?view=recommendations"]').first()
  ).toBeVisible();
  await expect(wide.locator('[data-mock="close"]')).toHaveCount(1);
  await expect(wide.locator('.author-side, .author-close')).toHaveCount(0);
  await expectWoven(wide);
  await wide.close();

  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const phone = await theAuthorPage(page, page.getByRole('dialog', { name: 'Menu' }));
  await phone.setViewportSize({ width: 390, height: 844 });
  await expect(phone.locator('dialog[open]')).toHaveCount(0);
  // The same order on a phone, the photo straight after the title, and the close last.
  await expect.poll(() => partsInOrder(phone)).toEqual(ORDER);
  await expectWoven(phone);
  // Every way to reach him is a finger-sized target.
  await expect(async () => {
    const heights = await phone
      .locator('[data-mock="ctas"] > a, [data-mock="strip"] a')
      .evaluateAll((links) => links.map((link) => link.getBoundingClientRect().height));
    expect(heights).toHaveLength(6);
    for (const height of heights) expect(height).toBeGreaterThanOrEqual(44);
  }).toPass({ timeout: 20_000 });
  // Nothing on the page is wider than the phone, and the page never scrolls sideways.
  await expect(async () => {
    const overflow = await phone.evaluate(() => ({
      wide: Array.from(
        document.querySelectorAll('[data-testid="about-page"] *:not(dialog *)')
      ).filter((element) => element.getBoundingClientRect().right > window.innerWidth + 0.5).length,
      scroll: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    }));
    expect(overflow.wide).toBe(0);
    expect(overflow.scroll).toBeLessThanOrEqual(0);
  }).toPass({ timeout: 20_000 });
  await phone.close();

  // A link already sent to the window's address lands on the page, never a window.
  await openTheYard(page, '/?doc=author');
  await expect(page).toHaveURL(/\?view=about$/);
  await expect(page.getByTestId('about-page')).toBeVisible();
  await expect(page.locator('dialog[open]')).toHaveCount(0);
});

test('the three story cards end on one line with their pictures on its floor, and the personal rows alternate big photographs left and right, none drawn past its file', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await answerPosts(page);
  await openTheYard(page, '/?view=about');
  await expect(page.locator('[data-mock="aside"] img').first()).toBeVisible();
  // The three story cards: one height, and each picture's foot, its credit under it where it has one, on one line.
  const stories = await page.locator('[data-mock="lead-card"]').evaluateAll((cards) =>
    cards.slice(0, 3).map((card) => ({
      top: card.getBoundingClientRect().top,
      height: card.getBoundingClientRect().height,
      foot: card.querySelector('[data-mock="aside"] figure')?.getBoundingClientRect().bottom ?? NaN,
    }))
  );
  expect(stories).toHaveLength(3);
  for (const card of stories) {
    expect(Math.abs(card.top - stories[0].top)).toBeLessThanOrEqual(1);
    expect(Math.abs(card.height - stories[0].height)).toBeLessThanOrEqual(2);
    expect(Math.abs(card.foot - stories[0].foot)).toBeLessThanOrEqual(2);
  }
  // The coaching card runs the box's full width on its own row, its drawing beside its words and never stretched.
  const coach = page.locator('[data-mock="coaching"]');
  const grid = await page.locator('[data-mock="lead-grid"]').boundingBox();
  const wide = await coach.boundingBox();
  expect(Math.abs(wide!.width - grid!.width)).toBeLessThanOrEqual(2);
  await expect(coach.locator('[data-mock="lead-picture"] img')).toHaveCSS('object-fit', 'contain');
  // The personal rows: photographs on the left, then the right, in turn, and each side the 7 or the 5 of the row.
  const rows = await page.locator('[data-mock="away-card"]').evaluateAll((all) =>
    all.map((row) => {
      const photos = row.querySelector('[data-mock="photos"]')!.getBoundingClientRect();
      const words = row.querySelector('h3')!.getBoundingClientRect();
      return { side: row.getAttribute('data-photo'), photos, words };
    })
  );
  expect(rows.map((row) => row.side)).toEqual([
    'left',
    'right',
    'left',
    'right',
    'left',
    'right',
    'wide',
  ]);
  for (const row of rows.slice(0, 6)) {
    if (row.side === 'left') expect(row.photos.right).toBeLessThan(row.words.left);
    else expect(row.photos.left).toBeGreaterThan(row.words.right);
  }
  expect(rows[0].photos.width).toBeGreaterThan(rows[1].photos.width * 1.3);
  // No photograph is drawn wider than the cut the browser loaded for it: each asks for its seat's width.
  await page.evaluate(async () => {
    for (const image of Array.from(document.querySelectorAll('[data-testid="about-page"] img'))) {
      image.scrollIntoView();
      await new Promise((resolve) => setTimeout(resolve, 60));
    }
  });
  await expect(async () => {
    const soft = await page
      .locator('[data-testid="about-page"] figure img')
      .evaluateAll((images) =>
        (images as HTMLImageElement[])
          .filter(
            (image) =>
              !image.src.endsWith('.svg') &&
              image.naturalWidth < image.getBoundingClientRect().width - 0.5
          )
          .map(
            (image) =>
              `${image.currentSrc} ${image.naturalWidth} < ${image.getBoundingClientRect().width}`
          )
      );
    expect(soft).toEqual([]);
  }).toPass({ timeout: 20_000 });
});
