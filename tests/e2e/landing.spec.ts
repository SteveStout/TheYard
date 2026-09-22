import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { landingTiles, MENU_ORDER } from '../../src/lib/siteMap';

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
    await rail.getByRole('button', { name: 'Inventory', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Inventory' })).toBeVisible();
    await expect(rail.locator('[aria-current="page"]')).toHaveText('Inventory');
  });
});
