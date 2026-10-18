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
  // Panels and blocks, not a letter: who he is, how he leads, the rest of his life in headed
  // blocks, a closing line, and what colleagues say standing aside.
  await expect(wide.locator('.author-panel')).toHaveCount(5);
  await expect(wide.getByRole('heading', { name: 'How I lead', exact: true })).toBeVisible();
  // The ten recommendations in full, read from /api/recommendations into a column on the right of
  // every other panel, running as far down the page as there are quotations.
  const said = wide.locator('.author-side');
  // The heading is the link to where they were written, and no prose stands under it.
  const saidHeading = said.getByRole('heading', { name: 'What colleagues say', exact: true });
  await expect(saidHeading).toBeVisible();
  await expect(saidHeading.getByRole('link')).toHaveAttribute(
    'href',
    'https://www.linkedin.com/in/stevenwstout/details/recommendations/'
  );
  await expect(said.locator('> p')).toHaveCount(0);
  await expect(said.locator('blockquote')).toHaveCount(10);
  await expect(said.locator('blockquote').first()).toContainText('pleasure of working with Steve');
  await expect(said).toContainText('Sarah Sebestyen');
  await expect(said.getByRole('link', { name: 'Sarah Sebestyen' })).toHaveAttribute(
    'href',
    /linkedin\.com\/in\//
  );
  // A quotation reaches the panel it stands in, here as on the landing page: the document's own
  // list indent is not a margin on the column.
  const inset = await said.evaluate((panel) => {
    const box = panel.getBoundingClientRect();
    const edge = getComputedStyle(panel);
    const card = panel.querySelector('li')!.getBoundingClientRect();
    return {
      left: Math.round(
        card.left - box.left - parseFloat(edge.borderLeftWidth) - parseFloat(edge.paddingLeft)
      ),
      right: Math.round(
        box.right - card.right - parseFloat(edge.borderRightWidth) - parseFloat(edge.paddingRight)
      ),
    };
  });
  expect(inset.left).toBeLessThanOrEqual(1);
  expect(Math.abs(inset.left - inset.right)).toBeLessThanOrEqual(1);
  const column = said.getByRole('region', { name: /recommendations, in full/ });
  await expect(column).toHaveAttribute('data-state', 'ready');
  // One scroll bar on the screen, the window's own: every recommendation stands in the page.
  expect(
    await column.evaluate((box) => [
      box.scrollHeight - box.clientHeight,
      box.getAttribute('tabindex'),
    ])
  ).toEqual([0, null]);
  const sideBox = (await said.boundingBox())!;
  const restBox = (await wide.locator('.author-rest').boundingBox())!;
  expect(sideBox.x).toBeGreaterThanOrEqual(restBox.x + restBox.width);
  // The eye reads it beside the panels; the document, and so the keyboard, reads it after the
  // introduction and before the rest.
  const order = await wide.evaluate((dialog) =>
    Array.from(dialog.querySelectorAll('.author-intro, .author-side, .author-rest')).map((node) =>
      node.classList.contains('author-intro')
        ? 'intro'
        : node.classList.contains('author-side')
          ? 'side'
          : 'rest'
    )
  );
  expect(order).toEqual(['intro', 'side', 'rest']);
  expect(await wide.locator('.author-block').count()).toBeGreaterThanOrEqual(13);
  await page.keyboard.press('Escape');

  await page.setViewportSize({ width: 375, height: 812 });
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const doc = await theAuthorPage(page, page.getByRole('dialog', { name: 'Menu' }));

  // On a phone the column follows who he is and comes before how he leads, every quotation in it.
  const introBox = (await doc.locator('.author-intro').boundingBox())!;
  const saidBox = (await doc.locator('.author-side').boundingBox())!;
  const leadBox = (await doc
    .locator('.author-panel', {
      has: page.getByRole('heading', { name: 'How I lead', exact: true }),
    })
    .boundingBox())!;
  expect(saidBox.y).toBeGreaterThan(introBox.y + introBox.height - 1);
  expect(leadBox.y).toBeGreaterThan(saidBox.y + saidBox.height - 1);
  await expect(doc.locator('.author-side blockquote')).toHaveCount(10);

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

  // Every picture is on the page, in its place: the vineyard beside the words, the drawing at the
  // top of How I lead, the lake and the
  // Pantheon in their blocks, the stream after History and the bridge after Games each on its own
  // row, each game's art, the grill and the doors in their blocks, the two rabbit pictures and
  // the two house plant pictures side by side, and the photographer's credit under the panels.
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
      { name: 'how-i-lead', hero: false, opens: true, paired: false },
      { name: 'ha-ha-tonka-castle-aerial', hero: false, opens: false, paired: false },
      { name: 'pantheon-at-dusk', hero: false, opens: false, paired: false },
      { name: 'couple-crossing-stream', hero: false, opens: false, paired: false },
      { name: 'mass-effect-legendary-edition', hero: false, opens: false, paired: false },
      { name: 'couple-on-wooden-bridge-wide', hero: false, opens: false, paired: false },
      { name: 'republic-commando-delta-squad', hero: false, opens: false, paired: true },
      { name: 'witcher-three-wild-hunt', hero: false, opens: false, paired: true },
      { name: 'grill-flames-on-driveway', hero: false, opens: false, paired: false },
      { name: 'interior-door-in-new-frame', hero: false, opens: false, paired: false },
      { name: 'couple-under-willow', hero: false, opens: false, paired: false },
      { name: 'rabbits-both-lying-on-runner', hero: false, opens: false, paired: true },
      { name: 'rabbits-lop-on-blue-rug', hero: false, opens: false, paired: true },
      { name: 'house-plants-under-grow-light', hero: false, opens: false, paired: true },
      { name: 'pothos-down-kitchen-shelves', hero: false, opens: false, paired: true },
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
  await expect(photos).toHaveCount(18);
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

  // Nothing on the page is wider than the phone. The column of quotations scrolls inside its
  // own box, down and never sideways, and is held to the phone like everything else.
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

test('the two half cards in each row end on one line, each photograph filling its card to the foot', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openTheYard(page, '/?doc=author');
  const doc = page.getByRole('dialog', { name: 'About Steven' });
  await expect(doc.getByTestId('author-page')).toBeVisible();
  await expect(doc.locator('.author-block-half img').first()).toBeVisible();
  // Every half card, with the row it sits in and how far its photograph's foot is from its padding.
  const cards = await doc.locator('.author-block-half').evaluateAll((blocks) =>
    blocks.map((block) => {
      const box = block.getBoundingClientRect();
      const photo = block.querySelector(':scope > .author-photo')?.getBoundingClientRect();
      const padding = parseFloat(getComputedStyle(block).paddingBottom);
      return {
        top: Math.round(box.top),
        height: box.height,
        gap: photo ? box.bottom - padding - photo.bottom : null,
      };
    })
  );
  const rows = new Map<number, typeof cards>();
  for (const card of cards) rows.set(card.top, [...(rows.get(card.top) ?? []), card]);
  const pairs = [...rows.values()].filter((row) => row.length === 2);
  expect(pairs.length).toBeGreaterThanOrEqual(2);
  for (const [left, right] of pairs) {
    expect(Math.abs(left.height - right.height)).toBeLessThanOrEqual(1);
    for (const card of [left, right]) {
      expect(card.gap).not.toBeNull();
      expect(card.gap!).toBeLessThanOrEqual(24);
    }
  }
});
