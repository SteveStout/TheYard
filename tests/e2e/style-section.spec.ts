import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The Style section's live blocks (ADR: The palette, the addendum on the four
 * pages): the Style guide's tiles carry their icons and open their pages in the
 * same window, the readouts hold the numbers the API counted, and Background and
 * ribbon draws its strip from the page's own ribbon component.
 */
test('the Style guide opens its sub-pages in the same window, and Background and ribbon draws its ribbon strip', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openTheYard(page, '/?doc=style-guide');
  const tiles = page.locator('dialog[open] [data-testid="style-tiles"] a');
  await expect(tiles).toHaveCount(3, { timeout: 30_000 });
  await expect(page.locator('dialog[open] [data-glyph] svg')).toHaveCount(3);
  const readouts = await page
    .locator('dialog[open] [data-testid="style-readout"]')
    .allTextContents();
  expect(readouts.slice(0, 3).every((value) => /^\d[\d,]*$/.test(value.trim()))).toBe(true);

  await tiles.nth(1).click();
  await expect(page.locator('dialog[open] h2').first()).toHaveText('Background and ribbon', {
    timeout: 30_000,
  });
  expect(page.url()).toContain('doc=background-ribbon');
  // Every spec hides the ribbons for its screenshots (tests/e2e/app.ts), so the strip is counted, not looked at.
  await expect
    .poll(() => page.locator('dialog[open] [data-testid="ribbon-strip"] svg path').count(), {
      timeout: 30_000,
    })
    .toBeGreaterThan(10);
});
