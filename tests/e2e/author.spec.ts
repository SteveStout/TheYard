import { expect, test, type Locator, type Page } from '@playwright/test';
import { openSection, openTheYard } from './app';

/**
 * The parts of the page in the order a hiring manager reads them, the one ask last (ADR: The sidebar, the addendums on
 * About Steven as a page and on the person beside the work).
 */
const ORDER = ['intro', 'proof', 'lead', 'voices', 'away', 'close'];

/**
 * The woven page at any width: three stories each inside its own lead card, the team drawing inside the coaching card,
 * seven away cards and then the strip as the away grid's only children, and all nineteen pictures.
 */
async function expectWoven(about: Page) {
  const lead = about.locator('[data-mock="lead-grid"] > [data-mock="lead-card"]');
  await expect(lead).toHaveCount(4);
  await expect(about.locator('[data-mock="lead-grid"] > *')).toHaveCount(4);
  for (const index of [0, 1, 2])
    await expect(lead.nth(index).locator('[data-mock="aside"]')).toHaveCount(1);
  await expect(about.locator('[data-mock="aside"]')).toHaveCount(3);
  await expect(about.locator('[data-mock="aside-title"]')).toHaveText([
    'The workshop',
    'The doors',
    'The lake, and no boat',
  ]);
  await expect(
    lead.nth(3).locator('[data-mock="lead-picture"] img[src*="how-i-lead"]')
  ).toHaveCount(1);
  const away = about.locator('[data-mock="away-grid"] > *');
  await expect(away).toHaveCount(8);
  await expect(about.locator('[data-mock="away-grid"] > [data-mock="away-card"] h3')).toHaveText([
    'History',
    'St. Charles',
    'Food',
    'Games',
    'Modding',
    'Too many house plants',
    'Freya and Vincent',
  ]);
  await expect(away.nth(7)).toHaveAttribute('data-mock', 'away-strip');
  await expect(about.locator('[data-mock="author-page"] img')).toHaveCount(19);
  await expect(about.locator('[data-mock="author-page"] figcaption')).toHaveCount(8);
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

test('About Steven opens from the sidebar and from the phone drawer, offers four ways to reach him, and fits a phone', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openTheYard(page);
  const wide = await theAuthorPage(page, page.getByTestId('side-rail'));
  // A page, not a window: nothing opens over it, and the parts stand in the manager's order.
  await expect(wide.locator('dialog[open]')).toHaveCount(0);
  await expect(wide.getByTestId('about-page').locator('[data-mock="eyebrow"]')).toHaveText(
    'Who and why · About Steven'
  );
  await expect.poll(() => partsInOrder(wide)).toEqual(ORDER);
  // Four ways to reach him under his words: the gold resume, Book 20 minutes, LinkedIn and GitHub.
  const moves = wide.locator('[data-mock="hero-moves"] > a');
  // Each button's own words: the PDF mark is a picture beside them, not part of the label.
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
    .toEqual(['Resume (PDF)', 'Book 20 minutes', 'LinkedIn', 'GitHub']);
  await expect(moves.nth(0)).toHaveAttribute('href', '/resume?pdf=1');
  await expect(moves.nth(1)).toHaveAttribute('href', '/api/go/book');
  await expect(moves.nth(2)).toHaveAttribute('href', 'https://www.linkedin.com/in/stevenwstout');
  await expect(moves.nth(3)).toHaveAttribute('href', 'https://github.com/SteveStout/TheYard');
  // The review route's three tiles, each door leaving for its case card there.
  const doors = wide.locator('[data-mock="proof"] > li a');
  await expect(doors).toHaveCount(3);
  for (const [index, card] of ['case-conway', 'case-storee', 'case-theyard'].entries())
    await expect(doors.nth(index)).toHaveAttribute('href', `/?view=review#${card}`);
  // Six voices, the link to every recommendation, and one close.
  await expect(wide.locator('[data-mock="voices"] [data-mock="voice"]')).toHaveCount(6);
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
  // The same order on a phone: the tiles keep their place under the hero, and the close is last.
  await expect.poll(() => partsInOrder(phone)).toEqual(ORDER);
  await expectWoven(phone);
  // Every way to reach him is a finger-sized target.
  await expect(async () => {
    const heights = await phone
      .locator('[data-mock="hero-moves"] > a')
      .evaluateAll((links) => links.map((link) => link.getBoundingClientRect().height));
    expect(heights).toHaveLength(4);
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

test('the three story cards end on one line with their pictures on its floor, and the away cards sit in even rows, photographs first', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
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
  // The coaching card runs the full width under them, its drawing beside its words and never stretched.
  const coach = page.locator('[data-mock="lead-card"]').nth(3);
  const grid = await page.locator('[data-mock="lead-grid"]').boundingBox();
  const wide = await coach.boundingBox();
  expect(Math.abs(wide!.width - grid!.width)).toBeLessThanOrEqual(2);
  await expect(coach.locator('[data-mock="lead-picture"] img')).toHaveCSS('object-fit', 'contain');
  // Away: the first two rows of three are each one height, and every card's photograph sits above its title.
  const cards = await page.locator('[data-mock="away-card"]').evaluateAll((all) =>
    all.map((card) => ({
      top: Math.round(card.getBoundingClientRect().top),
      height: card.getBoundingClientRect().height,
      photoFirst:
        (card.querySelector('img')?.getBoundingClientRect().top ?? Infinity) <
        (card.querySelector('h3')?.getBoundingClientRect().top ?? -Infinity),
    }))
  );
  expect(cards).toHaveLength(7);
  for (const row of [cards.slice(0, 3), cards.slice(3, 6)])
    for (const card of row) {
      expect(card.top).toBe(row[0].top);
      expect(Math.abs(card.height - row[0].height)).toBeLessThanOrEqual(2);
    }
  expect(cards.every((card) => card.photoFirst)).toBe(true);
  // No photograph is drawn past what its file fills at 2x: the Pantheon stops at 316 px.
  const pantheon = await page.locator('img[src*="pantheon"]').boundingBox();
  expect(pantheon!.width).toBeLessThanOrEqual(316.5);
});
