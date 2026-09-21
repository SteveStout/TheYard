import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { fadedContent, quietWordsOnTheBareGround } from './glass';

/**
 * The glass look (ADR: The glass look). Panels are slightly see-through over a
 * soft watermark, and every word, number and photograph is fully solid. The
 * second half is the rule, and it is held here against the rendered page and
 * not against the stylesheet: an opacity on a container fades everything in
 * it, whatever the stylesheet meant.
 */

test('every word and every photograph on the inventory is fully solid', async ({ page }) => {
  await openTheYard(page);
  await expect(page.locator('article img').first()).toBeVisible({ timeout: 30_000 });
  expect(await fadedContent(page)).toEqual([]);
  expect(await quietWordsOnTheBareGround(page)).toEqual([]);
  // A vehicle card is glass like every other surface: see-through in its ground, itself at full strength.
  const card = await page
    .locator('article')
    .first()
    .evaluate((element) => {
      const style = getComputedStyle(element);
      return { colour: style.backgroundColor, opacity: style.opacity };
    });
  expect(card.opacity).toBe('1');
  expect(card.colour).toMatch(/^rgba\(255, 255, 255, 0\.\d+\)$/);
  // And on a vehicle's own page, which puts the most words on the bare ground.
  await page.locator('article button').first().click();
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.getByText('Specifications')).toBeVisible({ timeout: 20_000 });
  expect(await quietWordsOnTheBareGround(page)).toEqual([]);
});

test('every word on the Admin tab is fully solid, and a tile is see-through in its ground only', async ({
  page,
}) => {
  await openTheYard(page, '/?view=admin');
  const tile = page.getByTestId('tile-health');
  await expect(tile).toHaveAttribute('data-tone', 'good', { timeout: 45_000 });
  await expect(page.getByTestId('traffic-stats')).toBeVisible({ timeout: 60_000 });
  expect(await fadedContent(page)).toEqual([]);
  expect(await quietWordsOnTheBareGround(page)).toEqual([]);
  // The see-through is in the background colour: an alpha below one there, and the tile itself at full strength.
  const ground = await tile.evaluate((element) => {
    const style = getComputedStyle(element);
    return { colour: style.backgroundColor, opacity: style.opacity };
  });
  expect(ground.opacity).toBe('1');
  expect(ground.colour).toMatch(/^rgba\(255, 255, 255, 0\.\d+\)$/);
});

test('the watermark is one drawing behind the page, with no words in it and no request behind it', async ({
  page,
}) => {
  const pictures: string[] = [];
  page.on('request', (request) => {
    if (request.resourceType() === 'image' && /watermark/i.test(request.url())) {
      pictures.push(request.url());
    }
  });
  await openTheYard(page);
  const watermark = page.getByTestId('watermark');
  await expect(watermark).toHaveCount(1);
  expect(await watermark.evaluate((svg) => (svg.textContent ?? '').trim())).toBe('');
  expect(await watermark.evaluate((svg) => getComputedStyle(svg).pointerEvents)).toBe('none');
  expect(await watermark.evaluate((svg) => getComputedStyle(svg).position)).toBe('fixed');
  expect(pictures).toEqual([]);
});
