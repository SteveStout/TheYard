import { expect, test } from '@playwright/test';
import { openSection, openTheYard } from './app';

/**
 * The Style section (ADR-016, the addendum on the style section): one page,
 * Colour and style, whose swatches are drawn from the token sheet when the
 * page is opened and are never a picture.
 */
test('the Style section opens Colour and style, and its swatches are the tokens the page is running on', async ({
  page,
}) => {
  await openTheYard(page);
  const rail = page.getByTestId('side-rail');
  await openSection(rail, 'Style');
  await rail.getByRole('button', { name: 'Colour and style' }).click();
  const doc = page.getByRole('dialog', { name: 'Colour and style' });
  await expect(doc.getByRole('heading', { level: 1, name: 'Colour and style' })).toBeVisible();
  await expect(page).toHaveURL(/doc=color-style/);

  // Every fence became a sheet, and between them they hold every colour token and the one gradient.
  const sheets = doc.getByTestId('swatches');
  await expect(sheets.first()).toBeVisible();
  const swatches = doc.locator('.swatch');
  expect(await swatches.count()).toBeGreaterThan(45);
  await expect(doc.locator('.swatch-missing')).toHaveCount(0);

  // A chip is painted with the token itself: its colour is whatever the page's own token is.
  const accent = swatches.filter({ hasText: '--color-accent-soft' }).first();
  const accentChip = accent.locator('.swatch-chip');
  const token = await page.evaluate(() => {
    const probe = document.createElement('span');
    probe.style.color = 'var(--color-accent-soft)';
    document.body.append(probe);
    const colour = getComputedStyle(probe).color;
    probe.remove();
    return colour;
  });
  // Read as a retried step: a document is drawn again a moment after it opens, and a chip found
  // before that has left the page and has no style to read (the gate for 1.0.0.171 met exactly that).
  await expect(async () => {
    expect(await accentChip.evaluate((element) => getComputedStyle(element).backgroundColor)).toBe(
      token
    );
  }).toPass({ timeout: 20_000 });
  // And the figures beside it are measured, in the shape the page promises.
  await expect(accent).toContainText(/#[0-9a-f]{6} · \d+\.\d{2} on white · \d+\.\d{2} on grey/);
  // The gradient is a sample of the one token, top to bottom.
  // Retried like the chip above: the precheck for 1.0.0.172 read this one as the empty string once.
  const gradient = swatches.filter({ hasText: '--gradient-header' });
  await expect(async () => {
    expect(
      await gradient
        .locator('.swatch-chip')
        .evaluate((chip) => getComputedStyle(chip).backgroundImage)
    ).toContain('linear-gradient');
  }).toPass({ timeout: 20_000 });
  // No picture of swatches anywhere on the page.
  await expect(doc.locator('img')).toHaveCount(0);
});
