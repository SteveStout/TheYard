import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';

// #region home-look
/**
 * The home page's look is an exit criterion (RULE 82; ADR-082, the addendum on the home page's order): the rules read
 * off its whole-page picture, held at every width the picture was measured at. A rule, never a typed figure.
 */
const WIDTHS = [
  [1920, 1080],
  [1568, 772],
  [1440, 900],
  [1280, 800],
  [1024, 768],
  [390, 844],
] as const;

/** Three made-up posts, so the newest-post line and Latest posts are drawn whatever the local store holds. */
const POSTS = [
  made('a-made-up-newest-post', 'A made-up newest post', 1),
  made('a-made-up-second-post', 'A made-up second post', 3),
  made('a-made-up-third-post', 'A made-up third post', 6),
];

type Box = { top: number; bottom: number; left: number; right: number; height: number };

/** The landing's direct sections, top to bottom, with whether each is Where next. */
async function sections(page: Page) {
  return page.getByTestId('landing').evaluate((landing) =>
    [...landing.children].map((section) => {
      const box = section.getBoundingClientRect();
      return {
        top: box.top + window.scrollY,
        bottom: box.bottom + window.scrollY,
        left: box.left,
        right: box.right,
        next: section.getAttribute('data-testid') === 'where-next',
      };
    })
  );
}

/** Each matching element's box on the page. */
async function boxes(page: Page, selector: string): Promise<Box[]> {
  return page.locator(selector).evaluateAll((all) =>
    all.map((each) => {
      const box = each.getBoundingClientRect();
      return {
        top: box.top + window.scrollY,
        bottom: box.bottom + window.scrollY,
        left: box.left,
        right: box.right,
        height: box.height,
      };
    })
  );
}

async function bottomOf(page: Page, testId: string): Promise<number> {
  const box = await page.getByTestId(testId).boundingBox();
  expect(box, `${testId} is drawn`).not.toBeNull();
  return box!.y + box!.height;
}

test('the home page is one unit at every width: shared edges, one gap, its height, Start here in one row', async ({
  page,
}) => {
  test.setTimeout(150_000);
  await answerBlog(page, POSTS);
  for (const [width, height] of WIDTHS) {
    await page.setViewportSize({ width, height });
    await openTheYard(page, '/');
    await expect(page.getByTestId('landing-newest')).toBeVisible();
    const at = `home at ${width}`;
    const parts = await sections(page);
    // 11. One unit: every direct section has the page's left and right edge.
    for (const part of parts) {
      expect(Math.abs(part.left - parts[0].left), `${at}: left edge`).toBeLessThanOrEqual(1);
      expect(Math.abs(part.right - parts[0].right), `${at}: right edge`).toBeLessThanOrEqual(1);
    }
    // 12. One gap: 20 between sections; Where next keeps its own 40.
    for (const [index, part] of parts.slice(1).entries()) {
      const gap = part.top - parts[index].bottom;
      expect(
        Math.abs(gap - (part.next ? 40 : 20)),
        `${at}: gap above ${index + 2} is ${gap}`
      ).toBeLessThanOrEqual(2);
    }
    // 13. Height: under 3,400 at 1568 and under 7,000 on a phone (the picture: 3,072 and 6,438).
    const tall = await page.evaluate(() => document.documentElement.scrollHeight);
    if (width === 1568) expect(tall, `${at}: page height`).toBeLessThan(3400);
    if (width === 390) expect(tall, `${at}: page height`).toBeLessThan(7000);
    // 16. Start here: four doors in one row of one height from 1280; two by two under it.
    const doors = await boxes(page, '[data-testid="landing-start"] li');
    expect(doors).toHaveLength(4);
    const firstRow = doors.filter((door) => Math.abs(door.top - doors[0].top) <= 1);
    expect(firstRow.length, `${at}: doors across`).toBe(width >= 1280 ? 4 : 2);
    for (const door of firstRow)
      expect(
        Math.abs(door.height - firstRow[0].height),
        `${at}: doors share a height`
      ).toBeLessThanOrEqual(1);
    // 15. Nothing scrolls sideways at any width.
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways, `${at}: sideways`).toBeLessThanOrEqual(0);
    // 14 and 15. The primary move is on the first screen at 1568 by 772 and at 390 by 844.
    if (width === 1568 || width === 390)
      expect(
        await bottomOf(page, 'landing-primary'),
        `${at}: the primary move`
      ).toBeLessThanOrEqual(height);
  }
});

// 14. At 1568 by 772: the name line, the heading, the primary move and the newest post are on the first screen, and
// the live readings end above its foot.
test('at 1568 by 772 the first screen says who this is, what he claims, what to press and the newest post', async ({
  page,
}) => {
  await answerBlog(page, POSTS);
  await page.setViewportSize({ width: 1568, height: 772 });
  await openTheYard(page, '/');
  for (const shown of ['landing-name', 'landing-primary', 'landing-newest', 'pulse-band'])
    expect(await bottomOf(page, shown), shown).toBeLessThanOrEqual(772);
  await expect(page.getByRole('heading', { level: 1 })).toBeInViewport();
  await expect(page.getByTestId('landing-name')).toHaveText(
    /Steven Stout\s*\/\s*Lead \/ Staff \.NET engineer\s*\/\s*Remote/
  );
  await expect(page.getByTestId('landing-primary')).toHaveAttribute('href', '/?view=review');
  // The newest post: its badge by the sidebar's own rule, its title, one link to the post.
  const newest = page.getByTestId('landing-newest');
  await expect(newest).toContainText('New post');
  await expect(newest).toContainText(POSTS[0].title);
  await expect(newest).toHaveAttribute('href', `/?view=blog&post=${POSTS[0].slug}`);
  await page.getByTestId('landing-primary').click();
  await expect(page).toHaveURL(/\?view=review$/);
});

// No post, or a read that fails: the newest-post line is not drawn.
test('the newest-post line is not drawn when the blog has no post', async ({ page }) => {
  await answerBlog(page, []);
  await openTheYard(page, '/');
  await expect(page.getByTestId('landing-primary')).toBeVisible();
  await expect(page.getByTestId('landing-newest')).toHaveCount(0);
});

// The dock: under 640 only, off at load, on once the moves have left the window, and the last section clear of it;
// the close's Book 20 minutes opens the booking sheet.
test('on a phone the home page’s dock comes on after the moves, and Book 20 minutes opens the sheet', async ({
  page,
}) => {
  await page.route('**/api/go/book', (route) => route.fulfill({ status: 204 }));
  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page, '/');
  const dock = page.getByTestId('review-dock');
  await expect(dock).toHaveAttribute('data-on', 'false');
  await expect(dock).toBeHidden();
  await page.getByTestId('landing-start').scrollIntoViewIfNeeded();
  await expect(dock).toHaveAttribute('data-on', 'true');
  await expect(dock.getByRole('link', { name: 'Check my work' })).toHaveAttribute(
    'href',
    '/?view=review'
  );
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  const next = await page.getByTestId('where-next').boundingBox();
  const bar = await dock.boundingBox();
  expect(next!.y + next!.height, 'the last section clears the dock').toBeLessThanOrEqual(
    bar!.y + 1
  );
  await page.getByTestId('review-close').getByRole('link', { name: 'Book 20 minutes' }).click();
  await expect(page.getByTestId('book-sheet')).toBeVisible();
  await expect(dock).toHaveCount(0);
});
// #endregion home-look
