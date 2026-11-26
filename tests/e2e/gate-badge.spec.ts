import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';

// #region gate-badge
/**
 * The gate badge (MASTER-PLAN-2026-11-17 R13e, R13g, R14): on every page, at the end of the store bar, what the
 * running version had to pass, read from what the build carries: the gate's word at every width and all three facts
 * from 1440, beside the store note's short form (store-toggle.spec holds that the note still fits); its name says all
 * three at any width, and the popover, fetched on its first open, says them in full and goes to the Quality page.
 * The words it may say are the ones gateFacts.ts can answer, so a build the gate has not finished (a gate
 * branch, this run) says pending instead of claiming green.
 */
const NAME =
  /^(Gate green|Gate pending), (Quality \d+ of \d+ held|Quality pending), CodeRabbit (reviewed|rate limited|not reviewed|pending): what v\d+\.\d+\.\d+\.\d+ had to pass$/;

test('every page carries the badge, which names all three and opens its popover on a click', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const path of ['/', '/?view=inventory', '/?doc=quality']) {
    await openTheYard(page, path);
    const badge = page.getByTestId('gate-badge');
    await expect(badge).toBeVisible();
    await expect(badge).toHaveAttribute('aria-label', NAME);
    // Green only when the gate's own reading of this version is on file; from 1440 all three facts are shown.
    const gated = (await badge.getAttribute('data-gated')) === 'true';
    await expect(badge).toHaveText(
      new RegExp(
        `^${gated ? 'Gate green' : 'Gate pending'} ?· (Quality \\d+ of \\d+ held|Quality pending) · CodeRabbit (reviewed|rate limited|not reviewed|pending)$`
      )
    );
    expect((await badge.boundingBox())!.width).toBeLessThan(560);
    // CodeRabbit's own word for this version rides on the badge, and the gap after the gate's word is a margin.
    await expect(badge).toHaveAttribute(
      'data-review',
      /^(full|rate limited|not reviewed|pending)$/
    );
    expect(
      await badge
        .locator('span')
        .first()
        .evaluate((span) => getComputedStyle(span).marginLeft)
    ).not.toBe('0px');
  }

  // From the landing: a document opens as a dialog over the page, and the badge waits under it like the rest.
  await openTheYard(page, '/');
  await page.getByTestId('gate-badge').click();
  const popover = page.getByTestId('gate-popover');
  await expect(popover).toBeVisible();
  await expect(popover.getByText(/^What v\d+\.\d+\.\d+\.\d+ had to pass$/)).toBeVisible();
  await expect(popover.getByRole('listitem')).toHaveCount(3);
  await expect(
    popover.getByRole('link', { name: 'See every number on the Quality page' })
  ).toHaveAttribute('href', '/?doc=quality');
  await expect(page.getByTestId('gate-badge')).toHaveAttribute('aria-expanded', 'true');
  await page.keyboard.press('Escape');
  await expect(popover).toHaveCount(0);
  // The focus goes back to the badge it was opened from (CodeRabbit on 1.0.5.82).
  await expect(page.getByTestId('gate-badge')).toBeFocused();
});

test("at 390 the badge is a thumb-tall chip in the note's row, and nothing scrolls sideways", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page, '/');
  const badge = page.getByTestId('gate-badge');
  await expect(badge).toBeVisible();
  // The gate's word alone on a phone: the other two facts are in its name and the popover.
  expect(await badge.evaluate((element) => element.innerText.trim())).toMatch(
    /^Gate (green|pending)$/
  );
  const box = (await badge.boundingBox())!;
  const note = (await page.getByTestId('store-bar-note').boundingBox())!;
  expect(box.height).toBeGreaterThanOrEqual(44);
  expect(Math.abs(box.y + box.height / 2 - (note.y + note.height / 2))).toBeLessThan(box.height);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});

// The popover draws over the glass panels after the header. Drawn inside the store bar, whose frost makes a
// stacking context, its layer counted only inside the bar, and the blog's filter panel, glass too and later in the
// page, painted over its lower half. So the topmost element at the panel's centre, and at a grid of points across
// it, has to be the popover's own, on the blog at the desk's width and at a phone's.
const POSTS = Array.from({ length: 12 }, (_, n) =>
  made(`a-post-${n + 1}`, `A post ${n + 1}`, n + 1, {
    topics: [['ai', 'dotnet', 'azure', 'career', 'security', 'linux'][n % 6]!],
    keywords: [['microsoft', 'copilot', 'claude', 'cqrs'][n % 4]!],
  })
);

for (const width of [1440, 390]) {
  test(`on the blog at ${width} the open popover is on top of the filter panel`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await answerBlog(page, POSTS);
    await openTheYard(page, '/?view=blog');
    await expect(page.getByTestId('blog-count')).toHaveText('Showing 12 of 12 posts');
    await page.getByTestId('gate-badge').click();
    const popover = page.getByTestId('gate-popover');
    await expect(popover).toBeVisible();
    const missed = await popover.evaluate((panel) => {
      const box = panel.getBoundingClientRect();
      const points = [0.5, 0.15, 0.3, 0.7, 0.85].flatMap((fx) =>
        [0.5, 0.15, 0.3, 0.7, 0.85].map((fy) => [fx, fy] as const)
      );
      return points
        .map(([fx, fy]) => {
          const x = box.left + box.width * fx;
          const y = box.top + box.height * fy;
          const hit = document.elementFromPoint(x, y);
          return hit && panel.contains(hit)
            ? null
            : `${fx},${fy}: ${hit?.className || hit?.tagName}`;
        })
        .filter((miss) => miss !== null);
    });
    expect(missed).toEqual([]);
    await page.keyboard.press('Escape');
    await expect(popover).toHaveCount(0);
    await expect(page.getByTestId('gate-badge')).toBeFocused();
  });
}

// #endregion gate-badge
