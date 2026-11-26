import { expect, test, type Locator, type Page } from '@playwright/test';
import { openSection, openTheYard } from './app';

/** The parts of the page in the order a hiring manager reads them (ADR: The sidebar, the addendum on About Steven as a page). */
const ORDER = ['intro', 'proof', 'lead', 'voices', 'close', 'away'];

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
  await expect(wide.locator('[data-mock="away"] .author-block')).toHaveCount(10);
  await wide.close();

  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const phone = await theAuthorPage(page, page.getByRole('dialog', { name: 'Menu' }));
  await phone.setViewportSize({ width: 390, height: 844 });
  await expect(phone.locator('dialog[open]')).toHaveCount(0);
  // The same order on a phone: the tiles keep their place under the hero.
  await expect.poll(() => partsInOrder(phone)).toEqual(ORDER);
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

test('the two half cards in each row end on one line, each photograph filling its card to the foot', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openTheYard(page, '/?view=about');
  const away = page.locator('[data-mock="away"]');
  await expect(away.locator('.author-block-half img').first()).toBeVisible();
  // Every half card, with the row it sits in, how far its photograph's foot is from its padding, and how tall it is.
  const cards = await away.locator('.author-block-half').evaluateAll((blocks) =>
    blocks.map((block) => {
      const box = block.getBoundingClientRect();
      const photo = block.querySelector(':scope > .author-photo')?.getBoundingClientRect();
      const padding = parseFloat(getComputedStyle(block).paddingBottom);
      return {
        top: Math.round(box.top),
        height: box.height,
        gap: photo ? box.bottom - padding - photo.bottom : null,
        image: block.querySelector('img')?.getBoundingClientRect().height ?? 0,
      };
    })
  );
  const rows = new Map<number, typeof cards>();
  for (const card of cards) rows.set(card.top, [...(rows.get(card.top) ?? []), card]);
  const pairs = [...rows.values()].filter((row) => row.length === 2);
  // The lake beside History; a block of two photographs takes one cell on this page, so Food sits beside Modding.
  expect(pairs.length).toBeGreaterThanOrEqual(1);
  for (const [left, right] of pairs) {
    expect(Math.abs(left.height - right.height)).toBeLessThanOrEqual(1);
    for (const card of [left, right]) {
      expect(card.gap).not.toBeNull();
      // The page caps every photograph so the section reads as a strip: a photograph fills its card to the
      // foot up to that cap, and the words beside it decide the rest.
      expect(card.gap!).toBeLessThanOrEqual(48);
      expect(card.image).toBeLessThanOrEqual(240.5);
    }
  }
});
