import { expect, test } from '@playwright/test';
import { openTheYard, openAllSections, openSection } from './app';

/**
 * Phone-sized viewport (iPhone-class, 375x812). Below 1024px the sidebar is a
 * drawer behind the header's hamburger (ADR-013), built from the same MENUS
 * record as the docked rail, and docs open full-screen. sidebar.spec proves
 * the rail; this file only proves the phone.
 */
test.use({ viewport: { width: 375, height: 812 } });

test('a phone gets one hamburger and no rail', async ({ page }) => {
  await openTheYard(page);
  await expect(page.getByRole('button', { name: 'Menu' })).toBeVisible();
  await expect(page.getByTestId('side-rail')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Hosting' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'CI/CD' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Best Practices' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Changelog' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'About' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Admin', exact: true })).toBeHidden();
});

test('the drawer lists every menu, opens a doc full-screen on a clear sheet with frosted panels, and closes on Escape', async ({
  page,
}) => {
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const drawer = page.getByRole('dialog', { name: 'Menu' });
  await expect(drawer).toBeVisible();
  for (const section of [
    'App Architecture',
    'Performance',
    'Style',
    'Hosting',
    'Built with AI',
    'CI/CD',
    'Best Practices',
    'Changelog',
    'About',
    'Author',
  ]) {
    await expect(drawer.getByRole('heading', { name: section, exact: true })).toBeVisible();
  }
  await expect(drawer.getByRole('link', { name: "Steven's resume (PDF)" })).toHaveAttribute(
    'href',
    '/api/docs/resume'
  );
  await expect(drawer.getByRole('link', { name: 'GitHub repository' })).toHaveAttribute(
    'href',
    'https://github.com/SteveStout/TheYard'
  );

  await openSection(drawer, 'Hosting');
  await drawer.getByRole('button', { name: 'Hosting overview' }).click();
  await expect(drawer).toBeHidden();
  const doc = page.getByRole('dialog', { name: 'Hosting' });
  await expect(doc.getByRole('heading', { level: 1, name: 'Hosting' })).toBeVisible();
  const box = await doc.boundingBox();
  expect(box?.width).toBe(375);
  expect(box?.height).toBe(812);
  // On a phone the sheet is frosted edge to edge, title bar included (1.0.3.24: the clear
  // sheet of the tweaks pass put dark type over the page's dark header), 78 per cent on
  // the sheet and on each panel, and a panel as wide as the screen.
  const read = await doc.evaluate((element) => {
    const panel = element.querySelector('.doc-panel');
    const box = panel?.getBoundingClientRect();
    return {
      sheet: getComputedStyle(element).backgroundColor,
      panel: panel === null ? '' : getComputedStyle(panel).backgroundColor,
      width: box?.width ?? 0,
    };
  });
  expect(read.sheet).toBe('rgba(255, 255, 255, 0.78)');
  expect(read.panel).toBe('rgba(255, 255, 255, 0.78)');
  expect(read.width).toBeGreaterThanOrEqual(373);

  await page.keyboard.press('Escape');
  await expect(doc).toBeHidden();
});

test('every drawer row leads with an icon, stands at least 44px tall, and the changelog opens from its section', async ({
  page,
}) => {
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  const drawer = page.getByRole('dialog', { name: 'Menu' });
  await expect(drawer).toBeVisible();

  // Every doc, the CI link, Admin, the resume and the repository: each row is
  // a button or a link carrying exactly one decorative (aria-hidden) svg.
  // Every section is closed on arrival since 1.0.0.135, so open them all: this
  // test is about what a row looks like, not about how many are showing.
  await openAllSections(drawer);
  const rows = drawer.locator('button:not([aria-label="Close"]), a');
  const count = await rows.count();
  expect(count).toBeGreaterThanOrEqual(24);
  // Read as one step over every row, not one round trip per row: seventy-odd sequential reads overran
  // the test's minute on a loaded machine (1.0.0.176's takes), with every row correct.
  await expect(async () => {
    const icons = await rows.evaluateAll((list) =>
      list.map((row) => row.querySelectorAll('svg[aria-hidden="true"]').length)
    );
    expect(icons).toHaveLength(count);
    expect(icons.filter((n) => n !== 1)).toEqual([]);
  }).toPass({ timeout: 20_000 });

  const changelogRow = drawer.getByRole('button', { name: 'Version history' });
  await changelogRow.scrollIntoViewIfNeeded();
  const rowBox = await changelogRow.boundingBox();
  expect(rowBox?.height).toBeGreaterThanOrEqual(44);
  const closeBox = await drawer.getByRole('button', { name: 'Close' }).boundingBox();
  expect(closeBox?.height).toBeGreaterThanOrEqual(44);
  expect(closeBox?.width).toBeGreaterThanOrEqual(44);

  await changelogRow.click();
  await expect(drawer).toBeHidden();
  await expect(
    page
      .getByRole('dialog', { name: 'Changelog' })
      .getByRole('heading', { level: 1, name: 'Changelog' })
  ).toBeVisible();
});

test('Admin is reachable from the drawer and the footer still renders', async ({ page }) => {
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  await page
    .getByRole('dialog', { name: 'Menu' })
    .getByRole('button', { name: 'Admin', exact: true })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page).toHaveURL(/view=admin/);
  // The timed checks fit a phone row too (ADR-010, second pass).
  await expect(page.getByTestId('health-card')).toContainText('healthy');
  await expect(page.getByTestId('check-duration').first()).toHaveText(/^\d+ ms$/);
  await expect(page.getByTestId('build-version')).toBeVisible();
  // The header brand is the way home from Admin on a phone too (ADR-017), and
  // home is the landing page since 1.0.1.0.
  await page
    .getByRole('banner')
    .getByRole('button', { name: /The Yard/ })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome to The Yard' })).toBeVisible();
});

test('the phone header has its own decision record, reachable from the drawer', async ({
  page,
}) => {
  await openTheYard(page);
  await page.getByRole('button', { name: 'Menu' }).click();
  // The records live in one collapsed index now (ADR-029); open it first.
  await page
    .getByRole('dialog', { name: 'Menu' })
    .locator('summary')
    .filter({ hasText: 'Decision Records' })
    .first()
    .click();
  await page
    .getByRole('dialog', { name: 'Menu' })
    .getByRole('button', { name: 'ADR: The phone header' })
    .click();
  await expect(
    page
      .getByRole('dialog', { name: 'ADR: The phone header' })
      .getByRole('heading', { level: 1, name: 'ADR: The phone header' })
  ).toBeVisible();
});

test('the Admin tiles are two to a row on a phone and nothing on the tab is wider than the phone', async ({
  page,
}) => {
  // The traffic card open and the machines card pinned under it: the two widest cards on the tab.
  await openTheYard(page, '/?view=admin&card=traffic&pin=machines');
  const strip = page.getByTestId('stat-strip');
  await expect(strip.getByTestId('tile-health')).toHaveAttribute('data-tone', 'good', {
    timeout: 45_000,
  });
  // Four rows of two (the tweaks pass, A2; two rows of four until then, when a label took three
  // lines in an 80 px tile): the first two side by side and the same size, the third under the
  // first, in the strip's own order.
  const tiles = strip.locator('li > [data-testid^="tile-"]');
  await expect(tiles).toHaveCount(8);
  const boxes = await Promise.all([0, 1, 2].map((index) => tiles.nth(index).boundingBox()));
  expect(boxes[1]?.y).toBe(boxes[0]?.y);
  expect(boxes[1]?.width).toBe(boxes[0]?.width);
  expect(boxes[2]?.x).toBe(boxes[0]?.x);
  expect(boxes[2]?.y ?? 0).toBeGreaterThan(boxes[0]?.y ?? 0);
  expect((boxes[1]?.x ?? 0) + (boxes[1]?.width ?? 0)).toBeLessThanOrEqual(375);
  // The traffic card's four numbers wear the same look and keep the same rule: two to a row.
  const stats = page.getByTestId('traffic-stats');
  await expect(stats).toBeVisible({ timeout: 60_000 });
  const requests = await stats.getByTestId('traffic-stat-requests').boundingBox();
  const typical = await stats.getByTestId('traffic-stat-typical').boundingBox();
  const slow = await stats.getByTestId('traffic-stat-slow').boundingBox();
  expect(requests?.y).toBe(typical?.y);
  expect(requests?.width).toBe(typical?.width);
  expect(slow?.x).toBe(requests?.x);
  expect(slow?.y ?? 0).toBeGreaterThan(requests?.y ?? 0);
  expect((typical?.x ?? 0) + (typical?.width ?? 0)).toBeLessThanOrEqual(375);
  // The pinned card is a fold under the open one on a phone; unfolded, the widest things on the tab are drawn,
  // and charts and tables scroll inside their cards.
  await page.getByTestId('bench-pin-fold').click();
  await expect(page.getByTestId('machines-card')).toBeVisible({ timeout: 60_000 });
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(overflow).toBe(0);
});

test('on a phone the Admin cards are a drawer behind the Cards button, and a card chosen there opens (the workbench)', async ({
  page,
}) => {
  await openTheYard(page, '/?view=admin&card=timing');
  await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', 'timing');
  // No rail beside the card on a phone: it is behind a thumb-sized button.
  await expect(page.getByTestId('bench-rail')).toHaveCount(0);
  const cards = page.getByTestId('bench-cards');
  await expect(async () => {
    expect((await cards.boundingBox())?.height ?? 0).toBeGreaterThanOrEqual(44);
  }).toPass();
  await cards.click();
  const drawer = page.getByRole('dialog', { name: 'Admin cards' });
  await expect(drawer).toBeVisible();
  await expect(drawer.getByTestId('bench-link-timing')).toHaveAttribute('aria-current', 'page');
  await drawer.getByTestId('bench-link-errors').click();
  await expect(drawer).toBeHidden();
  await expect(page.getByTestId('bench-open')).toHaveAttribute('data-card', 'errors');
  await expect(page).toHaveURL(/[?&]card=errors(&|$)/);
  // Nothing on the tab is wider than the phone with the drawer's button in the bar.
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(overflow).toBe(0);
});

test('on a phone the Admin tab holds a screen while its chunk is on the way, so the footer stays below it', async ({
  page,
}) => {
  // A phone that waited 1.9 s for the tab's chunk saw the footer on its first screen and then
  // pushed off it when the tab arrived (0.062 of shift on 1.0.3.21). The chunk is held back here.
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(/AdminPanel/, async (route) => {
    await held;
    await route.continue();
  });
  // Not openTheYard: it waits for the page's load event, and a built page counts the held chunk
  // in that (its preload), so it would wait for the release this test gives only after reading.
  await page.goto('/?view=admin', { waitUntil: 'domcontentloaded' });
  await expect(page.getByText('Reading the machines...')).toBeVisible();
  const footer = await page.locator('[data-frame="footer"]').boundingBox();
  expect(footer, 'the footer is drawn while the tab is on its way').not.toBeNull();
  expect(footer?.y ?? 0).toBeGreaterThanOrEqual(812);
  release();
  await expect(page.getByTestId('workbench')).toBeVisible();
});

test('the Admin and Account pills are a thumb tall on a phone', async ({ page }) => {
  // 44 px is the touch target the intro strip's pills already meet; on the desk
  // the same pills stay at 34 so the Admin rows stay dense (Steve, 2026-09-22).
  await openTheYard(page, '/?view=admin');
  const strip = page.getByTestId('stat-strip');
  await expect(strip.getByTestId('tile-health')).toHaveAttribute('data-tone', 'good', {
    timeout: 45_000,
  });
  for (const pill of [
    page.getByRole('button', { name: 'Back to inventory' }),
    page.getByTestId('strip-window-1h'),
  ]) {
    await expect(async () => {
      const box = await pill.boundingBox();
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
    }).toPass();
  }
  await openTheYard(page, '/?view=account');
  await expect(async () => {
    const box = await page.getByRole('button', { name: 'Back to inventory' }).boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }).toPass();
});

test('the landing page says what this is and who built it, and the inventory carries no welcome banner (the tweaks pass, A4)', async ({
  page,
}) => {
  // The landing page owns the sentence; the inventory opens on its own title and filters.
  await openTheYard(page, '/');
  const landing = page.getByTestId('landing');
  await expect(landing).toBeVisible();
  await expect(landing).toContainText('Steven Stout');
  const lede = await landing.locator('p').first().boundingBox();
  expect((lede?.y ?? 9999) + (lede?.height ?? 0)).toBeLessThanOrEqual(812);
  await openTheYard(page);
  await expect(page.getByTestId('intro-strip')).toHaveCount(0);
  const title = await page.getByRole('heading', { level: 1, name: 'Inventory' }).boundingBox();
  expect((title?.y ?? 9999) + (title?.height ?? 0)).toBeLessThanOrEqual(812);
});

// #region no-sideways-scroll
/**
 * A document does not scroll sideways on a phone (Steve, 2026-09-22: "the readme has
 * this weird left to right scrolling ... it shouldn't scroll left right on mobile").
 * The README's dialog was 375 wide and scrolled to 460, pushed by the long file paths
 * its lists carry in inline code. A code block is allowed its own sideways scroll,
 * because breaking a command in half is worse than sliding it, and the block keeps
 * that scroll inside its own box.
 */
test.describe('a document on a phone', () => {
  test.use({ viewport: { width: 375, height: 812 } });

  test('never scrolls sideways, whatever its longest word is', async ({ page }) => {
    for (const doc of ['readme', 'performance', 'adr-landing-page', 'changelog']) {
      await openTheYard(page, `/?doc=${doc}`);
      const dialog = page.getByRole('dialog').first();
      await expect(dialog).toBeVisible();
      await expect
        .poll(
          async () =>
            dialog.evaluate((open) => {
              const body = open.querySelector('[class*="dialogBody"]') ?? open;
              return body.scrollWidth - body.clientWidth;
            }),
          { message: `${doc} scrolls sideways inside its dialog` }
        )
        .toBeLessThanOrEqual(1);
      const page_overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth
      );
      expect(page_overflow, `${doc} widens the page itself`).toBe(0);
    }
  });
});
// #endregion no-sideways-scroll

// #region about-from-home
// Steve's iPhone, 25 September, on 1.0.3.25: About Steven opened from the home page sat
// about 100 px high, its title above the screen and the home page's resume tile under
// its foot. The phone's dialog is pinned to all four edges and the page behind it is
// frosted. No emulator has a phone's toolbars; this holds what one can: the sheet
// covers the screen edge to edge, top to bottom, its title is on the screen, and what
// is behind it is frost, after the home page was scrolled before the tile was pressed.
test('About Steven opened from the home page covers the whole phone screen, its title on it, frost behind', async ({
  page,
}) => {
  await openTheYard(page, '/');
  const tile = page.getByTestId('landing-tile-author');
  await tile.scrollIntoViewIfNeeded();
  await page.mouse.wheel(0, 400);
  await tile.click();
  const sheet = page.locator('dialog[open]').last();
  await expect(sheet.locator('.author-panel').first()).toBeVisible();
  await expect(async () => {
    const read = await sheet.evaluate((dialog) => {
      const box = dialog.getBoundingClientRect();
      const title = dialog.querySelector('[class*="dialogHeader"]')?.getBoundingClientRect();
      return {
        top: box.top,
        left: box.left,
        right: window.innerWidth - box.right,
        bottom: window.innerHeight - box.bottom,
        title: title === undefined ? -1 : title.top,
        behind: getComputedStyle(dialog, '::backdrop').backgroundColor,
      };
    });
    expect([read.top, read.left, read.right, read.bottom]).toEqual([0, 0, 0, 0]);
    // The title bar on the screen, at its top, inside the sheet's border (3 px measured).
    expect(read.title).toBeGreaterThanOrEqual(0);
    expect(read.title).toBeLessThanOrEqual(4);
    expect(read.behind).toBe('rgba(255, 255, 255, 0.78)');
  }).toPass({ timeout: 15_000 });
});
// #endregion about-from-home

// #region scale-band
// The styling pass moved the phone step from 600 to 640 (one scale of six steps). At 620,
// inside the band that moved, a document fills the screen as it does on any phone and the
// page does not scroll sideways (the reviewer of 25 September: no test stood in the band).
test('at 620, under the 640 step, a document fills the screen and nothing scrolls sideways', async ({
  page,
}) => {
  await page.setViewportSize({ width: 620, height: 900 });
  await openTheYard(page, '/?doc=hosting');
  const sheet = page.locator('dialog[open]').last();
  await expect(sheet.locator('.doc-panel').first()).toBeVisible();
  const box = await sheet.boundingBox();
  expect(box?.x).toBe(0);
  expect(box?.width).toBe(620);
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBe(0);
});
// #endregion scale-band

// #region vehicle-order
// The tweaks pass (A5): under 1024 a buyer reads the bid before the photos. Title,
// bid, photos, specifications, condition, seller, top to bottom, and the status
// line once, in the bid panel, where the header repeated it.
test('a vehicle on a phone reads title, bid, photos, specifications, condition, seller, the status once', async ({
  page,
}) => {
  await openTheYard(page);
  await page.locator('article button').first().click();
  const title = page.getByRole('heading', { level: 1 });
  await expect(title).toBeVisible();
  await expect(page.getByTestId('vehicle-layout')).toBeVisible();
  const tops = await page.evaluate(() => {
    const top = (selector: string) =>
      document.querySelector(selector)?.getBoundingClientRect().top ?? -1;
    return [
      top('h1'),
      top('section[aria-label="Auction"]'),
      top('section[aria-label="Photos"]'),
      top('section[aria-label="Specifications"]'),
      top('section[aria-label="Condition"]'),
      top('section[aria-label="Seller"]'),
    ];
  });
  expect(tops.every((value) => value >= 0)).toBe(true);
  expect([...tops].sort((a, b) => a - b)).toEqual(tops);
  // And every section the full width of the column (1.0.3.26 shrank Specifications to half
  // the screen on Steve's iPhone when the phone order became CSS).
  const widths = await page.getByTestId('vehicle-layout').evaluate((layout) => {
    const full = layout.getBoundingClientRect().width;
    return Array.from(layout.querySelectorAll('section[aria-label]'))
      .filter((section) => section.parentElement?.closest('section') === null)
      .map((section) => ({
        name: section.getAttribute('aria-label'),
        short: Math.round(full - section.getBoundingClientRect().width),
      }))
      .filter((section) => section.short > 1);
  });
  expect(widths, 'sections narrower than the column').toEqual([]);
  // The header carries no countdown or sold chip of its own under 1024.
  const header = page.locator('article header').first();
  await expect(header.locator('[class*="countdown"], [class*="soldChip"]')).toHaveCount(0);
});

// The self-review of 25 September: the phone order was a second tree, so turning a
// tablet across 1024 remounted the bid panel and dropped a typed amount and a bid in
// flight. The order is CSS now; the panel is the same element on both sides of 1024.
test('the bid panel is the same element on both sides of 1024, so a turn keeps what was typed', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openTheYard(page);
  await page.locator('article button').first().click();
  const panel = page.locator('section[aria-label="Auction"]');
  await expect(panel).toBeVisible();
  await panel.evaluate((node) => node.setAttribute('data-kept', 'yes'));
  await page.setViewportSize({ width: 800, height: 1100 });
  await expect(page.locator('section[aria-label="Photos"]')).toBeVisible();
  await expect(panel).toHaveAttribute('data-kept', 'yes');
  await page.setViewportSize({ width: 1280, height: 900 });
  await expect(panel).toHaveAttribute('data-kept', 'yes');
});
// #endregion vehicle-order

// #region one-line-readings
// 1.0.3.25, read on the live site at 390 after 1.0.3.24: "124 of 124" broke over two lines
// beside its ring, and a timing path broke every four characters. On a phone every strip
// tile's reading is one line, and a path in a table is never narrower than a short path.
test('a phone reads every strip tile on one line, and a table path is never squeezed', async ({
  page,
}) => {
  await openTheYard(page, '/?view=admin&card=timing');
  const strip = page.getByTestId('stat-strip');
  for (const tile of ['tile-pages', 'tile-memory', 'tile-health']) {
    await expect(strip.getByTestId(tile)).not.toHaveAttribute('data-tone', 'waiting', {
      timeout: 45_000,
    });
  }
  const lines = await strip.locator('[class*="tileValue_"]').evaluateAll((values) =>
    values.map((value) => {
      const style = getComputedStyle(value);
      const line = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.15;
      return {
        text: value.textContent ?? '',
        lines: Math.round(value.getBoundingClientRect().height / line),
      };
    })
  );
  for (const value of lines) expect(value.lines, `"${value.text}"`).toBeLessThanOrEqual(1);
  const path = page.getByTestId('timing-card').locator('td[class*="mono_"]').first();
  await expect(path).toBeVisible({ timeout: 30_000 });
  expect((await path.boundingBox())?.width ?? 0).toBeGreaterThanOrEqual(140);
});
// #endregion one-line-readings
