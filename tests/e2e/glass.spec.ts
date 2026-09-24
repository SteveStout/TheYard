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
  await openTheYard(page, '/?view=admin&card=traffic');
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

test('the ribbon ground is one drawing behind every view, from the rail edge, with no request behind it (ADR: The glass look)', async ({
  page,
}) => {
  const pictures: string[] = [];
  page.on('request', (request) => {
    if (request.resourceType() === 'image' && /ribbon|background|ground/i.test(request.url())) {
      pictures.push(request.url());
    }
  });
  const read = () =>
    page.evaluate(() => {
      const layer = document.querySelector('[data-testid="ribbons"]');
      const drawing = layer?.querySelector('svg');
      const rail = document.querySelector('[data-testid="side-rail"]');
      return {
        layers: document.querySelectorAll('[data-testid="ribbons"]').length,
        ground: getComputedStyle(document.body).backgroundImage,
        hidden: layer?.getAttribute('aria-hidden'),
        pointer: layer ? getComputedStyle(layer).pointerEvents : '',
        position: layer ? getComputedStyle(layer).position : '',
        words: (layer?.textContent ?? '').trim(),
        left: drawing ? Math.round(drawing.getBoundingClientRect().left) : -1,
        railRight: rail ? Math.round(rail.getBoundingClientRect().right) : 0,
        animation: drawing ? getComputedStyle(drawing).animationName : '',
      };
    });

  // A desk: the rail is docked, and the ribbons start where it ends.
  // Every spec opens with the ground hidden (tests/e2e/app.ts); this one keeps it. It stands
  // still for every reader, not only one who asked for less motion, so the page asks for none.
  await page.addInitScript(() => {
    (window as unknown as { __yardRibbons?: boolean }).__yardRibbons = true;
  });
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await page.setViewportSize({ width: 1280, height: 900 });
  await openTheYard(page);
  await expect(async () => {
    const desk = await read();
    expect(desk.layers).toBe(1);
    expect(desk.ground).toContain('linear-gradient');
    expect(desk.hidden).toBe('true');
    expect(desk.pointer).toBe('none');
    expect(desk.position).toBe('fixed');
    expect(desk.words).toBe('');
    expect(desk.railRight).toBeGreaterThan(0);
    expect(Math.abs(desk.left - desk.railRight)).toBeLessThanOrEqual(40);
    expect(desk.animation).toBe('none');
  }).toPass({ timeout: 20_000 });

  // The Admin tab and a document keep it: it lives in the shell, not in a view.
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('ribbons')).toHaveCount(1);
  await openTheYard(page, '/?doc=color-style');
  await expect(page.getByTestId('ribbons')).toHaveCount(1);
  // Every document carries the ground and stands its words on panels over it (1.0.2.0).
  await expect(page.getByTestId('ribbons-dialog')).toHaveCount(1);
  await expect(page.getByTestId('doc-page').locator('.doc-panel').first()).toBeVisible();
  // The Author page carries its own copy, fixed to its dialog, so the ground shows behind it too.
  await openTheYard(page, '/?doc=author');
  await expect(page.getByTestId('ribbons-dialog')).toHaveCount(1);
  await expect(page.getByTestId('ribbons-dialog')).toBeVisible();

  // A phone: the rail is the drawer, so the ribbons run from the screen's edge.
  await page.setViewportSize({ width: 375, height: 812 });
  await openTheYard(page);
  await expect(async () => {
    const phone = await read();
    expect(phone.layers).toBe(1);
    expect(Math.abs(phone.left)).toBeLessThanOrEqual(40);
  }).toPass({ timeout: 20_000 });

  // Nothing moves on a phone either.
  expect((await read()).animation).toBe('none');
  expect(pictures).toEqual([]);
});
