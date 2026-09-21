import { expect, test, type Locator, type Page } from '@playwright/test';
import { openSection, openTheYard } from './app';

/**
 * The Author section (ADR: The sidebar, the addendum on the author's
 * section): one page, About Steven, the last section of the sidebar in both
 * of its shapes. It is read on a phone first, so the phone is where its
 * width is held.
 */
async function theAuthorPage(page: Page, menu: Locator): Promise<Locator> {
  await openSection(menu, 'Author');
  await menu.getByRole('button', { name: 'About Steven' }).click();
  const doc = page.getByRole('dialog', { name: 'About Steven' });
  await expect(doc.getByTestId('author-page')).toBeVisible();
  await expect(page).toHaveURL(/doc=author/);
  return doc;
}

test('About Steven opens from the sidebar and from the phone drawer, offers three ways to reach him, and fits a phone', async ({
  page,
}) => {
  await openTheYard(page);
  const wide = await theAuthorPage(page, page.getByTestId('side-rail'));
  // Panels and blocks, not a letter: who he is, the rest of his life in headed blocks, a closing line.
  await expect(wide.locator('.author-panel')).toHaveCount(3);
  expect(await wide.locator('.author-block').count()).toBeGreaterThanOrEqual(4);
  await page.keyboard.press('Escape');

  await page.setViewportSize({ width: 375, height: 812 });
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const doc = await theAuthorPage(page, page.getByRole('dialog', { name: 'Menu' }));

  // Three ways to reach him, each a full finger-sized target, none of them an email address or a form.
  const buttons = doc.locator('.author-intro .author-button');
  await expect(buttons).toHaveCount(3);
  await expect(buttons.nth(0)).toHaveAttribute('href', '/api/docs/resume');
  await expect(buttons.nth(1)).toHaveAttribute('href', 'https://www.linkedin.com/in/stevenwstout');
  await expect(buttons.nth(2)).toHaveAttribute('href', 'https://github.com/SteveStout/TheYard');
  // Measured as one retried step, for the reason the photographs are below: a document is drawn
  // again a moment after it opens, and a button found before that is not the one on the page.
  await expect(async () => {
    const heights = await buttons.evaluateAll((links) =>
      links.map((link) => link.getBoundingClientRect().height)
    );
    expect(heights).toHaveLength(3);
    for (const height of heights) expect(height).toBeGreaterThanOrEqual(44);
  }).toPass({ timeout: 20_000 });
  await expect(doc.locator('a[href^="mailto:"], form, input')).toHaveCount(0);

  // All four photographs are on the page, in the mock's places: the stream beside the words, the
  // bridge opening the blocks, the two rabbit pictures side by side, and the credit under the panels.
  await expect(async () => {
    const places = await doc.locator('.author-photo').evaluateAll((figures) =>
      figures.map((figure) => ({
        name: figure.getAttribute('data-photo'),
        hero: figure.parentElement?.classList.contains('author-hero') ?? false,
        opens: figure.parentElement?.classList.contains('author-panel') ?? false,
        paired: figure.parentElement?.classList.contains('author-pair') ?? false,
      }))
    );
    expect(places).toEqual([
      { name: 'couple-crossing-stream', hero: true, opens: false, paired: false },
      { name: 'couple-on-wooden-bridge-wide', hero: false, opens: true, paired: false },
      { name: 'rabbits-both-lying-on-runner', hero: false, opens: false, paired: true },
      { name: 'rabbits-both-sitting-hallway', hero: false, opens: false, paired: true },
    ]);
  }).toPass({ timeout: 20_000 });
  await expect(doc.locator('.author-credit')).toHaveText(
    'Photos of Steve and Katie by McKinley Griggs.'
  );

  // Every photograph is served from this site, in the one frame, with words for it and its box reserved,
  // and a phone is never handed a file wider than 960.
  const photos = doc.locator('.author-photo img');
  await expect(photos).toHaveCount(4);
  const count = await photos.count();
  for (let index = 0; index < count; index++) {
    // Read as one retried step: the drawer that opened this document lets go of it as it closes, and
    // the document is drawn again, so a photograph found a moment ago may not be the one on the page.
    await expect(async () => {
      const photo = photos.nth(index);
      const read = await photo.evaluate((image: HTMLImageElement) => {
        image.scrollIntoView({ block: 'center' });
        return {
          framed: image.classList.contains('author-frame'),
          alt: image.alt,
          natural: image.naturalWidth,
          path: image.currentSrc === '' ? '' : new URL(image.currentSrc).pathname,
        };
      });
      expect(read.framed).toBe(true);
      expect(read.alt.length).toBeGreaterThan(15);
      expect(read.natural).toBeGreaterThan(0);
      expect(read.path).toMatch(/^\/api\/images\/author\/[a-z-]+-(480|960)\.(webp|jpg)$/);
      expect(read.natural).toBeLessThanOrEqual(960);
    }).toPass({ timeout: 20_000 });
  }

  // The headed blocks alternate by their order: no two neighbours wear the same top edge.
  // Read as retried steps, like every read inside a document: it is drawn again a moment after it opens.
  await expect(async () => {
    const tops = await doc
      .locator('.author-block')
      .evaluateAll((blocks) => blocks.map((block) => getComputedStyle(block).borderTopColor));
    expect(tops.length).toBeGreaterThanOrEqual(4);
    for (let index = 1; index < tops.length; index++) {
      expect(tops[index]).not.toBe(tops[index - 1]);
    }
  }).toPass({ timeout: 20_000 });

  // Nothing on the page is wider than the phone.
  await expect(async () => {
    const overflow = await doc.evaluate((dialog) => {
      const wide = Array.from(dialog.querySelectorAll('*')).filter(
        (element) => element.getBoundingClientRect().right > window.innerWidth + 0.5
      ).length;
      return {
        wide,
        scroll: dialog.scrollWidth - dialog.clientWidth,
        width: Math.round(dialog.getBoundingClientRect().width),
      };
    });
    expect(overflow.wide).toBe(0);
    expect(overflow.scroll).toBeLessThanOrEqual(0);
    // And the page has the whole phone, as every document does.
    expect(overflow.width).toBe(375);
  }).toPass({ timeout: 20_000 });
});
