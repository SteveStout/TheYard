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
      await expect(page.getByTestId('landing')).toContainText('showcase');

      const names = await page
        .locator('[data-testid^="landing-tile-"]')
        .evaluateAll((tiles) =>
          tiles.map((tile) => tile.getAttribute('data-testid')!.replace('landing-tile-', ''))
        );
      expect(names).toEqual(EXPECTED);

      // The sections under their three group headings, in the sidebar's groups (1.0.1.4).
      await expect(page.getByTestId('landing').getByRole('heading', { level: 2 })).toHaveText(
        SITE_GROUPS.map((group) => group.label)
      );

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

      // The evidence strip: four figures this build's own gate produced (1.0.2.0).
      const strip = page.getByTestId('landing-proof');
      await expect(strip).toBeVisible();
      await expect(strip.locator('li')).toHaveCount(4);
      await expect(page.getByTestId('landing-proof-tests')).toContainText(/\d[\d,]* *tests/);
      await expect(page.getByTestId('landing-proof-gate')).toContainText('one gate');
      await expect(page.getByTestId('landing-proof-records')).toContainText('decision records');

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
  await page.waitForLoadState('networkidle');
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
