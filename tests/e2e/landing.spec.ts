import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { landingTiles, MENU_ORDER, SITE_GROUPS } from '../../src/lib/siteMap';

/**
 * The landing page (1.0.1.0): a bare address opens it, and it is drawn from the
 * site map the sidebar is drawn from, so this reads the expected tiles off the
 * map rather than keeping a list of its own that could drift from both.
 */
const { featured, grid } = landingTiles();
const tileName = (tile: (typeof grid)[number]) =>
  tile.kind === 'section' ? tile.section.menu : tile.action.key;
const EXPECTED = [...featured, ...grid].map(tileName);

for (const viewport of [
  { width: 375, height: 812 },
  { width: 1280, height: 900 },
]) {
  test.describe(`at ${viewport.width}`, () => {
    test.use({ viewport });

    test('a bare address opens the landing page, with a tile per place in the map', async ({
      page,
    }) => {
      await openTheYard(page, '/');
      await expect(
        page.getByRole('heading', { level: 1, name: 'Welcome to The Yard' })
      ).toBeVisible();
      await expect(page.getByTestId('view-announcement')).toHaveText('The Yard, home');
      await expect(page.getByTestId('landing')).toContainText('Steven Stout');
      await expect(page.getByTestId('landing')).toContainText('built and run by one engineer');

      const names = await page
        .locator('[data-testid^="landing-tile-"]')
        .evaluateAll((tiles) =>
          tiles.map((tile) => tile.getAttribute('data-testid')!.replace('landing-tile-', ''))
        );
      expect(names).toEqual(EXPECTED);

      // The sections under their three group headings, in the sidebar's groups (1.0.1.4), after the
      // Tech stack table under the proof strip (1.0.4.31).
      await expect(page.getByTestId('landing').getByRole('heading', { level: 2 })).toHaveText([
        'What colleagues say',
        'Tech stack',
        ...SITE_GROUPS.map((group) => group.label),
      ]);

      // The two large tiles wear a photograph in the badge, and it has loaded.
      for (const name of ['inventory', 'author']) {
        const photo = page.getByTestId(`landing-tile-${name}`).locator('img');
        await expect(photo).toHaveCount(1);
        await expect
          .poll(() => photo.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth))
          .toBeGreaterThan(0);
      }

      // The one live reading: the Admin tile's health dot, read from /api/health.
      await expect(page.getByTestId('landing-admin-health')).toHaveText(/^(Healthy|Degraded)$/);

      // The band over it reads the running site: four instruments, each with a figure or the
      // word for a figure the server could not read, and never a zero standing in for one
      // (ADR: One reading for the first screen).
      const band = page.getByTestId('pulse-band');
      await expect(band).toBeVisible();
      // The band's labels name their own instrument and are not sections of the page: a reader
      // moving by heading should find the page's sections and nothing else.
      await expect(band.getByRole('heading')).toHaveCount(0);
      for (const instrument of ['pulse-latency', 'pulse-cost', 'pulse-roll', 'pulse-people']) {
        await expect(page.getByTestId(instrument)).toBeVisible();
      }
      // The roll instrument always has a figure: the container knows its own uptime and build.
      await expect(page.getByTestId('pulse-roll-figure')).toHaveText(
        /^(\d+ d \d+ h|\d+ h \d+ m|\d+ m)$/
      );
      // The build the reading came from: the shipped version and its commit, or the words this
      // site writes outside a build, as the footer and the rail write them.
      await expect(band).toContainText(/(dev build|\d+\.\d+\.\d+\.\d+ at [0-9a-f]{7})/);
      // A figure that was read is a number; one that was not says so in words.
      for (const [testId, pattern] of [
        ['pulse-latency', /^(\d[\d,]*ms|unread)$/],
        ['pulse-cost', /^(\$[\d,]+\.\d\d|[\d,]+\.\d\d [A-Z]{3}|unread)$/],
        // People who have visited, all time: a count, or the day the count began while nothing is
        // counted yet, or unread (ADR: People who have visited, counted once).
        ['pulse-people', /^(\d[\d,]*people,alltime|countingfrom\d+[A-Z][a-z]{2}|unread)$/],
      ] as const) {
        // The label, the reading, then the line under it: the reading is the second.
        const reading = (await page.getByTestId(testId).locator('p').nth(1).innerText()).replace(
          /\s+/g,
          ''
        );
        expect(reading, `${testId} reads a figure or says it is unread`).toMatch(pattern);
      }

      // The evidence strip: three figures this build's own gate produced (1.0.2.0), the gate's own
      // seconds having moved to the Admin tab.
      const strip = page.getByTestId('landing-proof');
      await expect(strip).toBeVisible();
      await expect(strip.locator('li')).toHaveCount(3);
      await expect(page.getByTestId('landing-proof-tests')).toContainText(/\d[\d,]* *tests/);
      await expect(page.getByTestId('landing-proof-records')).toContainText('decision records');
      // The two figures that are a share of a whole carry a ring, its number in words (the operator's look).
      await expect(page.getByTestId('landing-proof-ring-tests')).toHaveAttribute(
        'aria-label',
        /^[\d,]+ of [\d,]+ tests passed$/
      );
      await expect(page.getByTestId('landing-proof-ring-stores')).toHaveAttribute(
        'aria-label',
        /^\d of \d stores up$/
      );

      // What colleagues say: his ten LinkedIn recommendations, each one whole and credited, read
      // from /api/recommendations into a column of the page itself, with nothing to click through
      // to. Wide, the column stands to the right of everything else and runs as far down as there
      // are quotations; narrow, it follows the featured tiles.
      const said = page.getByTestId('landing-recommendations');
      await expect(said).toBeVisible();
      // Its heading is the link to where they were written, centred over the column, and a
      // quotation fills the panel: it starts where the panel's own edge ends, and the same gap
      // stands on both sides of it.
      await expect(
        said.getByRole('heading', { name: 'What colleagues say' }).getByRole('link')
      ).toHaveAttribute('href', /linkedin\.com\/in\/stevenwstout/);
      const area = await said.evaluate((column) => {
        const box = column.getBoundingClientRect();
        const edge = getComputedStyle(column);
        const inset = {
          left: parseFloat(edge.borderLeftWidth) + parseFloat(edge.paddingLeft),
          right: parseFloat(edge.borderRightWidth) + parseFloat(edge.paddingRight),
        };
        const heading = column.querySelector('h2')!.getBoundingClientRect();
        const card = column.querySelector('li')!.getBoundingClientRect();
        return {
          left: Math.round(heading.left - box.left),
          right: Math.round(box.right - heading.right),
          card: Math.round(card.left - box.left - inset.left),
          cardRight: Math.round(box.right - card.right - inset.right),
        };
      });
      expect(Math.abs(area.left - area.right)).toBeLessThanOrEqual(2);
      expect(area.card).toBeLessThanOrEqual(1);
      expect(Math.abs(area.card - area.cardRight)).toBeLessThanOrEqual(1);

      await expect(said.locator('blockquote')).toHaveCount(10);
      await expect(said.locator('li').first()).toContainText('Sarah Sebestyen');
      const column = said.getByRole('region', {
        name: /What colleagues say: \d+ recommendations, in full/,
      });
      await expect(column).toHaveAttribute('data-state', 'ready');
      // One scroll bar on the screen, the page's own: every quotation stands in the page, so the
      // column scrolls neither down nor sideways and holds no tab stop of its own.
      expect(
        await column.evaluate((box) => [
          box.scrollHeight - box.clientHeight,
          box.scrollWidth - box.clientWidth,
          box.getAttribute('tabindex'),
        ])
      ).toEqual([0, 0, null]);
      const saidBox = (await said.boundingBox())!;
      const stripBox = (await page.getByTestId('landing-proof').boundingBox())!;
      const authorBox = (await page.getByTestId('landing-tile-author').boundingBox())!;
      if (viewport.width >= 1280) {
        expect(saidBox.x).toBeGreaterThanOrEqual(stripBox.x + stripBox.width);
        expect(saidBox.y).toBeLessThan(stripBox.y);
      } else {
        expect(saidBox.y).toBeGreaterThan(authorBox.y + authorBox.height - 1);
        expect(stripBox.y).toBeGreaterThan(saidBox.y + saidBox.height - 1);
      }
      // Whole, not a sentence of each: the first one carries words from past its opening line, and
      // the credit under it says how the two worked together and when.
      await expect(said.locator('blockquote').first()).toContainText(
        'His technical expertise and practical approach'
      );
      await expect(said.locator('li').first()).toContainText(/November 2024\./);
      await expect(said.getByRole('button')).toHaveCount(0);
      // Every name opens the page of the person who wrote it, in a tab of its own. Counted inside
      // the quotations: the heading over them is a link too, to where all ten were written.
      const writers = said.locator('li').getByRole('link');
      await expect(writers).toHaveCount(10);
      await expect(writers.first()).toHaveText('Sarah Sebestyen');
      await expect(writers.first()).toHaveAttribute('href', /linkedin\.com\/in\//);
      await expect(writers.first()).toHaveAttribute('target', '_blank');

      // The resume is a large tile, and it opens the PDF this site serves.
      const resume = page.getByTestId('landing-tile-resume');
      await expect(resume).toHaveAttribute('href', /resume/);
      await expect(resume).toHaveAttribute('target', '_blank');

      // Every tile a full touch target, and nothing wider than the screen.
      await expect(async () => {
        const heights = await page
          .locator('[data-testid^="landing-tile-"]')
          .evaluateAll((tiles) => tiles.map((tile) => tile.getBoundingClientRect().height));
        expect(Math.min(...heights)).toBeGreaterThanOrEqual(44);
        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth
        );
        expect(overflow).toBe(0);
      }).toPass();

      // The left column runs on one set of columns: on a desk the featured row and the strip
      // share three, read off the boxes rather than the stylesheet, so the third of each ends on
      // the same x. The groups stand under both columns in a band the page's width (2026-10-23):
      // three across, the third ending where the quotations column ends. On a phone the featured
      // tiles stack one per row and the strip stays two across.
      const rights = async (selector: string) =>
        page
          .locator(selector)
          .evaluateAll((items) =>
            items.map((item) => Math.round(item.getBoundingClientRect().right))
          );
      const featuredRights = await rights(
        '[data-testid="landing"] ul[aria-label="Start here"] > li'
      );
      const stripRights = await rights('[data-testid="landing-proof"] > li');
      const groupRights = await rights('[data-testid="landing-group-built"] li');
      const columnBox = (await page.getByTestId('landing-recommendations').boundingBox())!;
      const pageRight = Math.round(columnBox.x + columnBox.width);
      const wideBox = (await page.getByTestId('landing-wide').boundingBox())!;
      const leadBox = (await page.locator('ul[aria-label="Start here"]').boundingBox())!;
      if (viewport.width >= 1024) {
        expect(new Set(featuredRights.slice(0, 3)).size).toBe(3);
        expect(Math.abs(stripRights[2] - featuredRights[2])).toBeLessThanOrEqual(1);
        expect(new Set(groupRights.slice(0, 3)).size).toBe(3);
        expect(Math.abs(groupRights[2] - pageRight)).toBeLessThanOrEqual(1);
        // The band starts under both columns: below the last quotation, from the left column's
        // left edge to the quotations column's right.
        expect(wideBox.y).toBeGreaterThanOrEqual(columnBox.y + columnBox.height - 1);
        expect(Math.abs(wideBox.x - leadBox.x)).toBeLessThanOrEqual(1);
        expect(Math.abs(wideBox.x + wideBox.width - pageRight)).toBeLessThanOrEqual(1);
        // No tile alone on a last row: the seventh tile of How it is built takes the row.
        const lefts = await page
          .locator('[data-testid="landing-group-built"] li')
          .evaluateAll((items) =>
            items.map((item) => Math.round(item.getBoundingClientRect().left))
          );
        expect(groupRights).toHaveLength(7);
        expect(Math.abs(lefts[6] - lefts[0])).toBeLessThanOrEqual(1);
        expect(Math.abs(groupRights[6] - pageRight)).toBeLessThanOrEqual(1);
      } else {
        expect(new Set(featuredRights).size).toBe(1);
        expect(new Set(stripRights.slice(0, 2)).size).toBe(2);
        expect(stripRights[2]).toBe(stripRights[0]);
      }
    });
  });
}

test('the Inventory tile opens the inventory at its own address, and Back returns home', async ({
  page,
}) => {
  await openTheYard(page, '/');
  await page.getByTestId('landing-tile-inventory').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Inventory' })).toBeVisible();
  await expect(page).toHaveURL(/view=inventory/);
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome to The Yard' })).toBeVisible();
});

test('a section tile opens the first document its sidebar section opens', async ({ page }) => {
  await openTheYard(page, '/');
  await page.getByTestId('landing-tile-author').click();
  await expect(page).toHaveURL(/doc=/);
  await expect(page.getByRole('dialog')).toBeVisible();
});

test('an address shared before the landing page existed still opens the inventory', async ({
  page,
}) => {
  await openTheYard(page, '/?body_style=coupe&title_status=clean');
  await expect(page.getByRole('heading', { level: 1, name: 'Inventory' })).toBeVisible();
  await expect(page.getByTestId('landing')).toHaveCount(0);
});

test.describe('the docked rail', () => {
  test.use({ viewport: { width: 1280, height: 900 } });

  test('follows the same map: its sections in map order, and an Inventory row', async ({
    page,
  }) => {
    await openTheYard(page, '/');
    const rail = page.getByTestId('side-rail');
    const headings = await rail.locator('summary h2').allTextContents();
    expect(headings.length).toBe(MENU_ORDER.length);
    // The same three groups as the landing page, each heading above its sections.
    for (const group of SITE_GROUPS) {
      await expect(rail.getByTestId(`rail-group-${group.key}`).locator('p').first()).toHaveText(
        group.label
      );
    }
    // The landing page's own row is the first one, current while it shows (Steve: "you also need the dashboard on the navigation").
    await expect(rail.locator('[aria-current="page"]')).toHaveText('Home');
    await rail.getByRole('button', { name: 'Inventory', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Inventory' })).toBeVisible();
    await expect(rail.locator('[aria-current="page"]')).toHaveText('Inventory');
    await rail.getByRole('button', { name: 'Home', exact: true }).click();
    await expect(
      page.getByRole('heading', { level: 1, name: 'Welcome to The Yard' })
    ).toBeVisible();
    await expect(rail.locator('[aria-current="page"]')).toHaveText('Home');
  });
});

// #region landing-asks-for-less
/**
 * The landing page does not pay for the inventory (1.0.3.0). Measured on the live
 * sites at 1.0.2.2: /api/vehicles was the slowest request the landing page made,
 * 450 ms on the SQL site and 897 ms on the Cosmos DB one, for a page that shows no
 * vehicle, and the filter options went with it. Opening the inventory asks for both,
 * because that is the view that shows them.
 */
test('Admin opened from the landing page says it goes back home, and does', async ({ page }) => {
  await openTheYard(page, '/');
  await page.getByTestId('landing-tile-admin').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  const back = page.getByRole('button', { name: 'Back to home' });
  await expect(back).toBeVisible();
  await back.click();
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome to The Yard' })).toBeVisible();
  // By its address there is nothing behind it, and the button says where it goes instead.
  await openTheYard(page, '/?view=admin');
  await expect(page.getByRole('button', { name: 'Back to inventory' })).toBeVisible();
});

// #region no-shift
/**
 * Nothing moves after the first paint (1.0.3.7). The store bar, the version
 * line, a document's dialog and the loading inventory each reserve their
 * height before their answer arrives; this reads the page's own layout-shift
 * entries after it has settled and holds the sum near zero. Chromium only:
 * WebKit reports no layout-shift entries, and a test that always passes there
 * would prove nothing, so the number is held where the entries exist. Four
 * tests rather than a loop, because the README's count of browser tests is
 * counted by the `test(` lines. The fourth is the landing page on a phone
 * (1.0.3.10): the store bar's note row arrived with /api/stores and moved the
 * page 38 px at 390 in half the runs, which the desk widths never showed.
 */
type Shifts = { supported: boolean; total: number; moved: string[] };

/** The layout shifts a view had after its first paint, with what moved, so a failure names the element. */
async function shiftAfterFirstPaint(
  page: Page,
  path: string,
  viewport: { width: number; height: number } = { width: 1280, height: 900 }
): Promise<Shifts> {
  await page.setViewportSize(viewport);
  await page.addInitScript(() => {
    const shifts: Shifts = {
      supported: PerformanceObserver.supportedEntryTypes.includes('layout-shift'),
      total: 0,
      moved: [],
    };
    (window as unknown as { __shifts: Shifts }).__shifts = shifts;
    type Source = {
      node: Element | null;
      previousRect: DOMRectReadOnly;
      currentRect: DOMRectReadOnly;
    };
    type Shift = PerformanceEntry & { hadRecentInput: boolean; value: number; sources?: Source[] };
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as Shift[]) {
        if (entry.hadRecentInput) continue;
        shifts.total += entry.value;
        for (const source of entry.sources ?? []) {
          const node = source.node;
          const name = node
            ? `${node.tagName.toLowerCase()}${node.id ? '#' + node.id : ''}.${String(node.className).split(' ')[0]}`
            : '?';
          shifts.moved.push(
            `${entry.value.toFixed(3)} ${name} y ${Math.round(source.previousRect.y)}->${Math.round(source.currentRect.y)} h ${Math.round(source.previousRect.height)}->${Math.round(source.currentRect.height)}`
          );
        }
      }
    }).observe({ type: 'layout-shift', buffered: true });
  });
  await openTheYard(page, path);
  // Settled is a fact about the page, not half a second with no request open: every reading the
  // first paint waits for has arrived and drawn. On a view that carries none of them this still
  // holds, because each poll returns at once when its element is not on the page.
  await expect
    .poll(
      () =>
        page.evaluate(() => {
          const health = document.querySelector('[data-testid="landing-admin-health"]');
          const tests = document.querySelector('[data-testid="landing-proof-tests"]');
          const said = document.querySelector(
            '[data-testid="landing-recommendations"] [role="region"]'
          );
          return [
            health === null || /^(Healthy|Degraded|Unreachable)$/.test(health.textContent ?? ''),
            tests === null || /\d/.test(tests.textContent ?? ''),
            said === null || said.getAttribute('data-state') !== 'reading',
          ].every(Boolean);
        }),
      { timeout: 30_000 }
    )
    .toBe(true);
  await page.waitForTimeout(1500);
  return page.evaluate(() => (window as unknown as { __shifts: Shifts }).__shifts);
}

/**
 * Chromium reports layout-shift entries and WebKit does not, so on WebKit the
 * reading is that nothing was reported, which is not a proof and is not
 * claimed as one: the number is held where the entries exist.
 */
function holdStill(shifts: Shifts): void {
  if (!shifts.supported) {
    expect(shifts.total).toBe(0);
    return;
  }
  expect(shifts.total, `moved: ${shifts.moved.join(' | ')}`).toBeLessThan(0.02);
}

test('nothing on the landing page moves after its first paint', async ({ page }) => {
  holdStill(await shiftAfterFirstPaint(page, '/'));
});

test('nothing on the inventory moves after its first paint', async ({ page }) => {
  holdStill(await shiftAfterFirstPaint(page, '/?view=inventory'));
});

test('nothing on a document moves after its first paint', async ({ page }) => {
  holdStill(await shiftAfterFirstPaint(page, '/?doc=readme'));
});

test('nothing on the landing page moves after its first paint on a phone', async ({ page }) => {
  holdStill(await shiftAfterFirstPaint(page, '/', { width: 390, height: 664 }));
});
// #endregion no-shift

test('asks for no catalogue until the inventory opens', async ({ page }) => {
  const asked: string[] = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api/vehicles' || url.pathname === '/api/facets')
      asked.push(url.pathname);
  });

  await openTheYard(page, '/');
  await expect(page.getByTestId('landing-proof')).toBeVisible();
  expect(asked, 'the landing page asked for the inventory it does not show').toEqual([]);

  await page.getByTestId('landing-tile-inventory').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Inventory' })).toBeVisible();
  await expect.poll(() => asked.includes('/api/vehicles')).toBe(true);
});
// #endregion landing-asks-for-less

// #region tech-stack
/**
 * The Tech stack table on the home page (ADR: Technology versions, the addendum on the home page's
 * table): drawn from the generated table, every row with a version and a link to the file that proves
 * it on GitHub, the .NET 9 Function flagged, and on a phone every row stacked with no sideways scroll.
 */
for (const viewport of [
  { width: 1440, height: 900 },
  { width: 390, height: 844 },
]) {
  test(`the Tech stack table stands under the proof strip at ${viewport.width}`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    await openTheYard(page, '/');
    const stack = page.getByTestId('landing-stack');
    await expect(stack.getByRole('heading', { level: 2, name: 'Tech stack' })).toBeVisible();
    const table = stack.getByTestId('landing-stack-table');
    await expect(table.locator('tbody tr')).not.toHaveCount(0);
    for (const version of await table.getByTestId('stack-version').allInnerTexts()) {
      expect(version.trim()).not.toBe('');
    }
    for (const href of await table
      .locator('a')
      .evaluateAll((links) => links.map((link) => link.getAttribute('href') ?? ''))) {
      expect(href).toMatch(/^https:\/\/github\.com\/SteveStout\/TheYard\/(blob|tree)\/main\//);
    }
    await expect(stack.getByTestId('stack-backend')).toContainText('.NET and ASP.NET Core');
    const proof = await page.getByTestId('landing-proof').boundingBox();
    const box = await stack.boundingBox();
    expect(box!.y).toBeGreaterThan(proof!.y);
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways).toBeLessThanOrEqual(0);
  });
}
// #endregion tech-stack
