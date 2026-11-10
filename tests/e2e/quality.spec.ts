import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region quality
/**
 * The Quality page (ADR-130, Quality you can watch): at a desk's width and a phone's, every trend fence becomes a
 * drawn line with 1.0.4.115 marked, the rules table and CodeRabbit's log are there, no live number is left as a
 * note, and nothing scrolls sideways but the tables' own scrollers. The number of charts is read from the document
 * the API serves, never typed.
 */
for (const width of [1280, 390]) {
  test(`at ${width} the Quality page draws every number's line from the history and marks the ratchet`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    const served = await (await page.request.get('/api/docs/quality')).text();
    const fences = (served.match(/^```quality-trend/gm) ?? []).length;
    expect(fences).toBeGreaterThan(8);
    expect(served).not.toContain('(no live measure');
    expect(served).not.toContain('is not in this build');

    await openTheYard(page, '/?doc=quality');
    const dialog = page.locator('dialog[open]');
    await expect(dialog.locator('h2').first()).toHaveText('Quality you can watch', {
      timeout: 30_000,
    });
    await expect(dialog.getByTestId('quality-trend')).toHaveCount(fences, { timeout: 30_000 });
    // Every number's chart marks the version it joined the ratchet; the two coverage charts have no such version.
    await expect(dialog.getByTestId('quality-trend-marker')).toHaveCount(fences - 2);
    await expect(
      dialog.getByRole('heading', { name: 'The rules, and the tests that hold them' })
    ).toBeVisible();
    await expect(
      dialog.getByRole('heading', { name: 'CodeRabbit, the last reviewer' })
    ).toBeVisible();
    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(sideways).toBeLessThanOrEqual(0);
  });
}
// #endregion quality
