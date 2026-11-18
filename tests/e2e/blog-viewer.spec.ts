import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';

// #region blog-viewer
/**
 * The slide viewer (ADR: Every LinkedIn post is also a blog post, the addendum on the slide viewer): a slide in the
 * strip opens it at that slide in a native dialog, the arrow keys turn it and the address follows, Escape closes it
 * and the slide that opened it has the focus back, Tab never leaves it, a deep link opens it on its slide, axe finds
 * nothing on it open, and opening it and each slide shown are reported as the events BlogRules admits.
 */
const POST = made('a-post-with-slides', 'A post with slides', 1, { slides: 4 });

test('a slide in the strip opens the viewer there, the arrows turn it, and Escape hands the focus back', async ({
  page,
}) => {
  await answerBlog(page, [POST]);
  await openTheYard(page, `/?view=blog&post=${POST.slug}`);
  const thumb = page
    .getByTestId('blog-slides-strip')
    .getByRole('button', { name: /^Open slide 2 of 4/ });
  await thumb.click();
  const viewer = page.getByTestId('slide-viewer');
  await expect(viewer).toBeVisible();
  await expect(viewer.getByTestId('slide-count')).toHaveText('Slide 2 of 4');
  await expect(viewer.getByTestId('slide-caption')).toHaveText('Slide 2');
  await expect(page).toHaveURL(new RegExp(`post=${POST.slug}&slide=2$`));
  await page.keyboard.press('ArrowRight');
  await expect(viewer.getByTestId('slide-count')).toHaveText('Slide 3 of 4');
  await expect(page).toHaveURL(/slide=3$/);
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('ArrowLeft');
  await expect(viewer.getByTestId('slide-count')).toHaveText('Slide 1 of 4');
  await expect(viewer.getByRole('button', { name: 'Previous slide' })).toBeDisabled();
  for (let n = 0; n < 20; n++) await page.keyboard.press('Tab');
  expect(await viewer.evaluate((dialog) => dialog.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Escape');
  await expect(viewer).toHaveCount(0);
  await expect(page).toHaveURL(new RegExp(`post=${POST.slug}$`));
  await expect(thumb).toBeFocused();
});

test('a deep link opens the viewer on its slide, and the strip on the film shows which', async ({
  page,
}) => {
  await answerBlog(page, [POST]);
  await openTheYard(page, `/?view=blog&post=${POST.slug}&slide=3`);
  const viewer = page.getByTestId('slide-viewer');
  await expect(viewer.getByTestId('slide-count')).toHaveText('Slide 3 of 4');
  await expect(viewer.getByRole('button', { name: 'Slide 3' })).toHaveAttribute(
    'aria-current',
    'true'
  );
  await viewer.getByRole('button', { name: 'Slide 4' }).click();
  await expect(viewer.getByTestId('slide-count')).toHaveText('Slide 4 of 4');
  await viewer.getByRole('button', { name: 'Close the slides' }).click();
  await expect(viewer).toHaveCount(0);
  // The site did not push this entry, so turning a slide must not mark it as one it did: All posts opens the list in place.
  await page.getByRole('button', { name: 'All posts' }).click();
  await expect(page).toHaveURL(/\?view=blog$/);
  await expect(page.getByTestId('blog-count')).toBeVisible();
});

test('the zoom is a button too, so a keyboard zooms the slide and zooms it back', async ({
  page,
}) => {
  await answerBlog(page, [POST]);
  await openTheYard(page, `/?view=blog&post=${POST.slug}&slide=2`);
  const viewer = page.getByTestId('slide-viewer');
  const zoom = viewer.getByRole('button', { name: 'Zoom', exact: true });
  await zoom.focus();
  await page.keyboard.press('Enter');
  await expect(zoom).toHaveAttribute('aria-pressed', 'true');
  await expect(viewer.getByTestId('slide-image')).toHaveCSS('transform', /matrix\(2.2, 0, 0, 2.2/);
  await page.keyboard.press('Enter');
  await expect(zoom).toHaveAttribute('aria-pressed', 'false');
  await expect(viewer.getByTestId('slide-image')).toHaveCSS('transform', 'none');
});

test('the open viewer is axe clean', async ({ page }) => {
  await answerBlog(page, [POST]);
  await openTheYard(page, `/?view=blog&post=${POST.slug}&slide=1`);
  await expect(page.getByTestId('slide-viewer')).toBeVisible();
  const results = await new AxeBuilder({ page })
    .include('[data-testid="slide-viewer"]')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  expect(
    results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target).join(' ')}`)
  ).toEqual([]);
});

test('opening the viewer is a click on slides, and each slide shown is a slide event', async ({
  page,
}) => {
  await answerBlog(page, [POST]);
  const events: { type: string; value: string }[] = [];
  await page.route('**/api/blog/events', async (route) => {
    events.push(route.request().postDataJSON() as { type: string; value: string });
    await route.fulfill({ status: 204 });
  });
  await openTheYard(page, `/?view=blog&post=${POST.slug}`);
  await page.getByTestId('slides-view-all').click();
  await page.keyboard.press('ArrowRight');
  await expect
    .poll(() => events.some((e) => e.type === 'click' && e.value === 'slides'))
    .toBe(true);
  await expect
    .poll(() => events.filter((e) => e.type === 'slide').map((e) => e.value))
    .toEqual(expect.arrayContaining(['1', '2']));
});
// #endregion blog-viewer

// #region blog-post-foot
/**
 * A single post's foot (ADR: Every LinkedIn post is also a blog post, the addendum on the read model): the older and the
 * newer post, and up to three that share a topic, each a card with its cover, its reading time and its reads.
 */
test('a post ends with its older and newer posts and up to three related by topic', async ({
  page,
}) => {
  const posts = [
    made('the-newest-post', 'The newest post', 1, { topics: ['ai'] }),
    made('the-middle-post', 'The middle post', 2, { topics: ['ai', 'testing'] }),
    made('the-oldest-post', 'The oldest post', 3, { topics: ['testing'] }),
    made('a-post-on-linux', 'A post on Linux', 4, { topics: ['linux'] }),
  ];
  await answerBlog(page, posts);
  await openTheYard(page, '/?view=blog&post=the-middle-post');
  await expect(page.getByTestId('blog-older')).toContainText('The oldest post');
  await expect(page.getByTestId('blog-newer')).toContainText('The newest post');
  const related = page.getByTestId('blog-related');
  await expect(related.getByTestId('blog-mini')).toHaveCount(2);
  await expect(related.getByTestId('blog-mini').first()).toContainText('3 min read');
  await expect(related.getByTestId('blog-mini').first().locator('img')).toHaveAttribute(
    'loading',
    'lazy'
  );
  await page.getByTestId('blog-older').click();
  await expect(page).toHaveURL(/post=the-oldest-post$/);
  await expect(page.getByTestId('blog-older')).toContainText('A post on Linux');
  await expect(page.getByTestId('blog-newer')).toContainText('The middle post');
});
// #endregion blog-post-foot
