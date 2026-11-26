import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { landingTiles, MENU_ORDER, SITE_GROUPS } from '../../src/lib/siteMap';
import { cardAddress } from '../../src/lib/recommendationsPage';
import { VOICES } from '../../src/lib/voices';
import data from '../../data/recommendations.json' with { type: 'json' };

/** The recommendations as the file holds them: the count and each voice's words are read, never typed. */
const WRITERS = data.recommendations;

/**
 * The landing page (1.0.1.0): a bare address opens it, and it is drawn from the
 * site map the sidebar is drawn from, so this reads the expected tiles off the
 * map rather than keeping a list of its own that could drift from both.
 */
const { doors, explore, grid } = landingTiles();
const tileName = (tile: (typeof grid)[number]) =>
  tile.kind === 'section' ? tile.section.menu : tile.action.key;
/** The tiles named landing-tile-*, in the page's order: the two photograph doors, then the small tiles by group. */
const EXPECTED = [
  ...doors.filter((door) => door.kind === 'section' || !door.action.banner),
  ...explore.flatMap((group) => group.tiles),
].map(tileName);

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
        page.getByRole('heading', {
          level: 1,
          name: 'Built with AI. Gated so quality never slides.',
        })
      ).toBeVisible();
      await expect(page.getByTestId('view-announcement')).toHaveText('The Yard, home');
      await expect(page.getByTestId('landing')).toContainText('Steven Stout');
      // The hook took the welcome card's place (ADR-132): its sentence is the heading above, its link the Quality page.
      await expect(page.getByTestId('hook')).toHaveAttribute('data-hook', 'landing');
      await expect(page.getByTestId('landing')).toContainText('built and run by one engineer');

      const names = await page
        .locator('[data-testid^="landing-tile-"]')
        .evaluateAll((tiles) =>
          tiles.map((tile) => tile.getAttribute('data-testid')!.replace('landing-tile-', ''))
        );
      expect(names).toEqual(EXPECTED);

      // The sections under their three group headings, in the sidebar's groups (1.0.1.4), after the
      // Tech stack table under the proof strip (1.0.4.31), each the shared section head with its number (ADR: The
      // sales pages set the site's look). Latest posts, 02, is not drawn while the local blog has no post; the close's
      // heading a screen reader lands on still says "Next step".
      await expect(page.getByTestId('landing').getByRole('heading', { level: 2 })).toHaveText([
        /^01\s*Start here$/,
        /^03\s*What colleagues say$/,
        /^04\s*Tech stack$/,
        ...SITE_GROUPS.map((group, index) => new RegExp(`^0${index + 5}\\s*${group.label}$`)),
        'Next step',
      ]);

      // The two large tiles wear a photograph in the badge, and it has loaded.
      for (const name of ['inventory', 'author']) {
        const photo = page.getByTestId(`landing-tile-${name}`).locator('img');
        await expect(photo).toHaveCount(1);
        await expect
          .poll(() => photo.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth))
          .toBeGreaterThan(0);
      }

      // Sign in and Admin are the sidebar's alone (ADR-082, the addendum on the home page's order).
      await expect(page.getByTestId('landing-tile-admin')).toHaveCount(0);
      await expect(page.getByTestId('landing-tile-account')).toHaveCount(0);

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
        // People who have visited: the kept visits since the first kept day (1.0.4.48), or a count of
        // people with the day the count began (one person, or people),
        // the day alone while nothing is counted yet, or unread (ADR: People who have visited,
        // counted once).
        [
          'pulse-people',
          /^(\d[\d,]*(visits?since\d+[A-Z][a-z]{2}|(people|person)(since\d+[A-Z][a-z]{2}|,alltime))|countingfrom\d+[A-Z][a-z]{2}|unread)$/,
        ],
      ] as const) {
        // The label, the reading, then the line under it: the reading is the second.
        const reading = (await page.getByTestId(testId).locator('p').nth(1).innerText()).replace(
          /\s+/g,
          ''
        );
        expect(reading, `${testId} reads a figure or says it is unread`).toMatch(pattern);
      }
      // The bill's under-line: the forecast in whole currency, which is the month so far plus the days
      // left and so never below it; "at least" the spend if a forecast ever reads under it; or nothing
      // while Azure has not forecast (ADR: What Azure charges, the addendum of 2026-10-24).
      const under = (await page.getByTestId('pulse-cost').locator('p').nth(2).innerText()).trim();
      expect(under, 'the bill reads a forecast, at least the spend, or nothing').toMatch(
        /^(forecast \$[\d,]+|at least \$[\d,]+\.\d\d|)$/
      );

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

      // What colleagues say: three named voices, each the answer's own words and one link to that person's card, and
      // the way to every one of them; the heading is the link to where they were written.
      const voices = page.getByTestId('landing-voices');
      await expect(
        voices.getByRole('heading', { name: 'What colleagues say' }).getByRole('link')
      ).toHaveAttribute('href', /linkedin\.com\/in\/stevenwstout/);
      await expect(voices.getByTestId('voice')).toHaveCount(VOICES.home.length);
      for (const [index, name] of VOICES.home.entries()) {
        const entry = WRITERS.find((writer) => writer.name === name)!;
        await expect(voices.getByTestId('voice').nth(index)).toHaveAttribute(
          'href',
          cardAddress(name)
        );
        await expect(voices.getByTestId('voice-pull').nth(index)).toHaveText(`"${entry.pull}"`);
      }
      const all = page.getByTestId('landing-recommendations-all');
      await expect(all).toContainText(`Read all ${WRITERS.length} named recommendations`);
      await expect(all).toHaveAttribute('href', '/?view=recommendations');
      await expect(page.getByTestId('landing-recommendations')).toHaveCount(0);

      // Start here: four doors in one row from 1280 (two by two under it), the review route first, each with what
      // it costs; the resume opens the PDF this site serves in a tab of its own, the review route its view in place.
      const doorLinks = page.getByTestId('landing-start').locator('li > *');
      await expect(doorLinks).toHaveCount(4);
      await expect(doorLinks).toHaveText([
        /Check my work[\s\S]*2 min/,
        /About Steven[\s\S]*3 min/,
        /Inventory[\s\S]*Live/,
        /resume \(PDF\)[\s\S]*PDF/,
      ]);
      const resume = page.getByTestId('landing-banner-resume');
      await expect(resume).toHaveAttribute('href', /resume/);
      await expect(resume).toHaveAttribute('target', '_blank');
      const review = page.getByTestId('landing-banner-review');
      await expect(review).toHaveAttribute('href', '/?view=review');
      await expect(review).not.toHaveAttribute('target', '_blank');

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

      // One unit: every direct section of the landing shares the page's left and right edges.
      const edges = await page.getByTestId('landing').evaluate((landing) =>
        [...landing.children].map((child) => {
          const box = child.getBoundingClientRect();
          return [Math.round(box.left), Math.round(box.right)];
        })
      );
      for (const [left, right] of edges) {
        expect(Math.abs(left - edges[0][0])).toBeLessThanOrEqual(1);
        expect(Math.abs(right - edges[0][1])).toBeLessThanOrEqual(1);
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
  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Built with AI. Gated so quality never slides.',
    })
  ).toBeVisible();
});

test('a section tile opens the first document its sidebar section opens', async ({ page }) => {
  await openTheYard(page, '/');
  await page.getByTestId('landing-tile-about').click();
  await expect(page).toHaveURL(/doc=/);
  await expect(page.getByRole('dialog')).toBeVisible();
  // A section that is one link, About Steven, is that link: the page of its own, never a window.
  await expect(page.getByTestId('landing-tile-author')).toHaveAttribute('href', '/?view=about');
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
      page.getByRole('heading', {
        level: 1,
        name: 'Built with AI. Gated so quality never slides.',
      })
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
  await page.getByTestId('side-rail').getByRole('button', { name: 'Admin', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  const back = page.getByRole('button', { name: 'Back to home' });
  await expect(back).toBeVisible();
  await back.click();
  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Built with AI. Gated so quality never slides.',
    })
  ).toBeVisible();
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
      node: Node | null;
      previousRect: DOMRectReadOnly;
      currentRect: DOMRectReadOnly;
    };
    type Shift = PerformanceEntry & { hadRecentInput: boolean; value: number; sources?: Source[] };
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as Shift[]) {
        if (entry.hadRecentInput) continue;
        shifts.total += entry.value;
        for (const source of entry.sources ?? []) {
          // A shift's source can be a text node, which has no tag: naming it by its node name
          // keeps the loop going, so every shift in the batch is counted.
          const node = source.node;
          const name =
            node instanceof Element
              ? `${node.tagName.toLowerCase()}${node.id ? '#' + node.id : ''}.${String(node.className).split(' ')[0]}`
              : node
                ? node.nodeName.toLowerCase()
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
 * it on GitHub, a piece on an older .NET than the site flagged, and on a phone every row stacked with no sideways scroll.
 */
for (const viewport of [
  { width: 1440, height: 900 },
  { width: 390, height: 844 },
]) {
  test(`the Tech stack table stands on its own page, and home carries its header and the way to it, at ${viewport.width}`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    await openTheYard(page, '/');
    // Home: the stack's header and chips, the link to the whole table, and no element of the table.
    const build = page.getByTestId('landing-build');
    await expect(build.getByRole('heading', { level: 2, name: 'Tech stack' })).toBeVisible();
    await expect(build.getByTestId('landing-stack-summary')).toContainText('What TheYard runs on');
    await expect(build.getByTestId('landing-stack-link')).toHaveAttribute('href', '/?view=stack');
    await expect(page.getByTestId('landing-stack-table')).toHaveCount(0);
    await expect(page.getByTestId('landing').locator('table')).toHaveCount(0);
    const proof = await page.getByTestId('landing-proof').boundingBox();
    const summary = await build.getByTestId('landing-stack-summary').boundingBox();
    expect(summary!.y).toBeGreaterThan(proof!.y);
    // The stack page: the whole table, every row with a version and a link to the file that proves it.
    await build.getByTestId('landing-stack-link').click();
    await expect(page).toHaveURL(/\?view=stack$/);
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
    await expect(page.getByTestId('stack-page').getByRole('heading', { level: 1 })).toHaveText(
      'What it runs on, with the file that proves each line.'
    );
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways).toBeLessThanOrEqual(0);
  });
}
// #endregion tech-stack
