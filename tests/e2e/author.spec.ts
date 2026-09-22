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

  // All ten pictures are on the page, in their places: the vineyard beside the words, the lake and the
  // Pantheon in their blocks, the stream after History and the bridge after Games each on its own row,
  // the game's art, the grill and the doors in their blocks, the two rabbit pictures
  // side by side, and the photographer's credit under the panels.
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
      { name: 'steve-and-katie-vineyard', hero: true, opens: false, paired: false },
      { name: 'ha-ha-tonka-castle-aerial', hero: false, opens: false, paired: false },
      { name: 'pantheon-at-dusk', hero: false, opens: false, paired: false },
      { name: 'couple-crossing-stream', hero: false, opens: false, paired: false },
      { name: 'mass-effect-legendary-edition', hero: false, opens: false, paired: false },
      { name: 'couple-on-wooden-bridge-wide', hero: false, opens: false, paired: false },
      { name: 'grill-flames-on-driveway', hero: false, opens: false, paired: false },
      { name: 'interior-door-in-new-frame', hero: false, opens: false, paired: false },
      { name: 'couple-under-willow', hero: false, opens: false, paired: false },
      { name: 'rabbits-both-lying-on-runner', hero: false, opens: false, paired: true },
      { name: 'rabbits-lop-on-blue-rug', hero: false, opens: false, paired: true },
      { name: 'st-charles-main-street-christmas', hero: false, opens: false, paired: false },
      { name: 'rabbits-under-hay-rack', hero: false, opens: false, paired: false },
    ]);
  }).toPass({ timeout: 20_000 });
  await expect(doc.locator('.author-credit')).toHaveText(
    'Photos of Steve and Katie at the stream, on the bridge and under the willow by McKinley Griggs.'
  );

  // Every photograph is served from this site, in the one frame, with words for it and its box reserved,
  // and a phone is never handed a file wider than 960.
  const photos = doc.locator('.author-photo img');
  await expect(photos).toHaveCount(13);
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
      expect(read.path).toMatch(/^\/api\/images\/author\/[a-z-]+-\d+\.(avif|webp|jpg)$/);
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

  // A photograph standing between the blocks keeps inside their edges on a phone (Steve's phone,
  // 2026-09-21: the stream photograph ran past the blocks on the right).
  await expect(async () => {
    const edges = await doc.evaluate((dialog) => {
      const blocks = Array.from(dialog.querySelectorAll('.author-block')).map((block) =>
        block.getBoundingClientRect()
      );
      const between = Array.from(dialog.querySelectorAll('.author-between .author-photo')).map(
        (photo) => photo.getBoundingClientRect()
      );
      return {
        left: Math.min(...blocks.map((box) => box.left)),
        right: Math.max(...blocks.map((box) => box.right)),
        photos: between.map((box) => [box.left, box.right]),
      };
    });
    expect(edges.photos.length).toBeGreaterThanOrEqual(1);
    for (const [left, right] of edges.photos) {
      expect(left).toBeGreaterThanOrEqual(edges.left - 1);
      expect(right).toBeLessThanOrEqual(edges.right + 1);
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
