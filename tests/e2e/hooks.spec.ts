import { expect, test, type Locator, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';
import file from '../../data/hooks.json' with { type: 'json' };

// #region hooks
/**
 * A hook on every page (ADR-132): on a phone, 390 by 844, each page a reader is sent to opens with its hook, and the
 * hook's whole box sits on the first screen with no scroll. The landing, /about, the resume door and a blog post each
 * draw their entry of data/hooks.json; the Quality page's hook is its own first chart, and the review route's is its
 * first screen's claim and its primary button (ADR: The review route gives a reason before it asks). The post is made up and
 * answered by the test, with a tall first image, so the box is held at its largest.
 */
const PHONE = { width: 390, height: 844 };
const lines = (page: string) => file.hooks.find((hook) => hook.page === page)!.lines;

const POST = made(
  'a-made-up-post-with-a-hook',
  'A made-up post whose title is long enough to wrap onto a second line on a phone',
  1,
  {
    summary:
      'Two sentences of summary, the length a real post carries. The second one is here so the box is as tall as a real one.',
    slides: 2,
  }
);

async function onTheFirstScreen(box: Locator, name: string) {
  await expect(box, name).toBeVisible({ timeout: 30_000 });
  const at = (await box.boundingBox())!;
  expect(at.y, `${name} starts on the page`).toBeGreaterThanOrEqual(0);
  expect(at.y + at.height, `${name} ends inside the first 844 px`).toBeLessThanOrEqual(
    PHONE.height
  );
}

async function atPhoneSize(page: Page) {
  await page.setViewportSize(PHONE);
}

test.describe('at 390 by 844 each hook sits on the first screen', () => {
  test('the landing: the name, the quality sentence and the primary move on the first screen, then the chart and its link', async ({
    page,
  }) => {
    await atPhoneSize(page);
    await openTheYard(page, '/');
    const hook = page.locator('[data-hook="landing"]');
    await expect(hook.getByRole('heading', { level: 1 })).toHaveText(lines('landing')[0]);
    await expect(hook.getByTestId('quality-trend')).toBeVisible();
    await expect(
      hook.getByRole('link', { name: 'Every number, on the Quality page' })
    ).toHaveAttribute('href', '/?doc=quality');
    // The split layout stacks on a phone (ADR-132, the addendum on the split hook): what must be on the first screen
    // is who this is, the claim and the one move to make; the chart follows them.
    await onTheFirstScreen(hook.getByTestId('landing-name'), 'the name line');
    await onTheFirstScreen(hook.getByRole('heading', { level: 1 }), 'the landing heading');
    await onTheFirstScreen(hook.getByTestId('landing-primary'), 'the primary move');
    // The live readouts stay directly below it.
    const band = (await page.getByTestId('pulse-band').boundingBox())!;
    const box = (await hook.boundingBox())!;
    expect(band.y).toBeGreaterThan(box.y + box.height - 1);
    expect(band.y - (box.y + box.height)).toBeLessThan(48);
  });

  test('/about: the resume two lines', async ({ page }) => {
    await atPhoneSize(page);
    await page.goto('/about');
    const hook = page.locator('[data-hook="about"]');
    for (const line of lines('about')) await expect(hook).toContainText(line);
    await onTheFirstScreen(hook, 'the About hook');
  });

  test('/resume: the quality sentence and the recommendations', async ({ page }) => {
    await atPhoneSize(page);
    await page.goto('/resume?src=email');
    const hook = page.locator('[data-hook="resume"]');
    await expect(hook).toContainText(lines('resume')[0]);
    await expect(hook.getByRole('link')).toHaveAttribute('href', '/recommendations');
    await onTheFirstScreen(hook, 'the resume door hook');
  });

  test('a blog post: its title, its summary and its first image', async ({ page }) => {
    await atPhoneSize(page);
    await answerBlog(page, [POST]);
    await openTheYard(page, `/?view=blog&post=${POST.slug}`);
    const hook = page.locator('[data-hook="post"]');
    await expect(hook).toContainText(POST.title);
    await expect(hook).toContainText(POST.summary);
    await expect(hook.locator('img')).toHaveAttribute('alt', 'Slide 1');
    await onTheFirstScreen(hook, 'the post hook');
  });

  test('/?doc=quality: the page own first chart', async ({ page }) => {
    await atPhoneSize(page);
    await openTheYard(page, '/?doc=quality');
    const chart = page
      .locator('dialog[open]')
      .getByTestId('quality-chart')
      .filter({ visible: true })
      .first();
    // The lede's four readouts stand over the chart (the quality1120 picture, mock-390.png): the chart starts on the
    // first screen, the slip and the ratchet in view, and runs past its foot.
    await expect(chart).toBeVisible({ timeout: 30_000 });
    const at = (await chart.boundingBox())!;
    expect(
      at.y + 160,
      'the Quality page first chart starts on the first screen'
    ).toBeLessThanOrEqual(PHONE.height);
  });

  test('the review route: its claim and its primary button, before the proof tiles', async ({
    page,
  }) => {
    await atPhoneSize(page);
    await openTheYard(page, '/?view=review');
    await onTheFirstScreen(page.getByRole('heading', { level: 1 }), 'the claim');
    await onTheFirstScreen(page.getByTestId('review-proof'), 'the primary button');
  });
});
// #endregion hooks
