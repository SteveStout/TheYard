import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The inventory grid sizes itself to the box it is in, not to the window.
 *
 * <para>A media query at 1024 gave three columns the moment the docked rail
 * appeared, which is exactly when the grid lost 270 pixels to it: cards came
 * out around 215 wide, wrapping every title, every badge row and every city
 * name. The window cannot see the rail, and it certainly cannot see the rail
 * being collapsed to icons, which hands the grid two hundred pixels back.</para>
 */
test.describe('the inventory grid at a laptop width', () => {
  test.use({ viewport: { width: 1024, height: 900 } });

  /** The top edge of each of the first three cards, which is what a row is. */
  const rowTops = (page: import('@playwright/test').Page) =>
    page
      .locator('article')
      .evaluateAll((cards) =>
        cards.slice(0, 3).map((card) => Math.round(card.getBoundingClientRect().top))
      );

  test('gives two columns while the rail is taking its 270 pixels', async ({ page }) => {
    await openTheYard(page);
    await expect(page.getByTestId('side-rail')).toBeVisible();

    const [first, second, third] = await rowTops(page);

    expect(second).toBe(first);
    expect(third).toBeGreaterThan(first);
  });

  test('leaves the price boxes wide enough to hold the word inside them', async ({ page }) => {
    await openTheYard(page);

    const max = await page.getByLabel('Maximum price').boundingBox();

    // "Max" plus the padding either side. It was seventy-five, and the box
    // rendered "Ma" with the rest cut off, on the most ordinary laptop width
    // there is.
    expect(max?.width ?? 0).toBeGreaterThan(90);
  });

  test('takes the third column back when the rail collapses', async ({ page }) => {
    await openTheYard(page);
    await page.getByRole('button', { name: 'Collapse the sidebar' }).click();
    // The rail animates; the grid reflows when it has finished.
    await expect(page.getByTestId('side-rail')).toHaveAttribute('data-collapsed', 'true');

    await expect
      .poll(async () => (await rowTops(page)).filter((top, _, all) => top === all[0]).length)
      .toBe(3);
  });
});
