import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region gate-badge
/**
 * The gate badge (MASTER-PLAN-2026-11-17 R13e, R13g, R14): on every page, at the end of the store bar, what the
 * running version had to pass, read from what the build carries: the gate's word at every width and all three facts
 * from 1440, beside the store note's short form (store-toggle.spec holds that the note still fits); its name says all
 * three at any width, and the popover, fetched on its first open, says them in full and goes to the Quality page.
 * The words it may say are the ones gateFacts.ts can answer, so a build the gate has not finished (a gate
 * branch, this run) says pending instead of claiming green.
 */
const NAME =
  /^(Gate green|Gate pending), (Quality \d+ of \d+ held|Quality pending), CodeRabbit (reviewed|not reviewed|pending): what v\d+\.\d+\.\d+\.\d+ had to pass$/;

test('every page carries the badge, which names all three and opens its popover on a click', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const path of ['/', '/?view=inventory', '/?doc=quality']) {
    await openTheYard(page, path);
    const badge = page.getByTestId('gate-badge');
    await expect(badge).toBeVisible();
    await expect(badge).toHaveAttribute('aria-label', NAME);
    // Green only when the gate's own reading of this version is on file; from 1440 all three facts are shown.
    const gated = (await badge.getAttribute('data-gated')) === 'true';
    await expect(badge).toHaveText(
      new RegExp(
        `^${gated ? 'Gate green' : 'Gate pending'} · (Quality \\d+ of \\d+ held|Quality pending) · CodeRabbit (reviewed|not reviewed|pending)$`
      )
    );
    expect((await badge.boundingBox())!.width).toBeLessThan(560);
  }

  // From the landing: a document opens as a dialog over the page, and the badge waits under it like the rest.
  await openTheYard(page, '/');
  await page.getByTestId('gate-badge').click();
  const popover = page.getByTestId('gate-popover');
  await expect(popover).toBeVisible();
  await expect(popover.getByText(/^What v\d+\.\d+\.\d+\.\d+ had to pass$/)).toBeVisible();
  await expect(popover.getByRole('listitem')).toHaveCount(3);
  await expect(
    popover.getByRole('link', { name: 'See every number on the Quality page' })
  ).toHaveAttribute('href', '/?doc=quality');
  await expect(page.getByTestId('gate-badge')).toHaveAttribute('aria-expanded', 'true');
  await page.keyboard.press('Escape');
  await expect(popover).toHaveCount(0);
});

test("at 390 the badge is a thumb-tall chip in the note's row, and nothing scrolls sideways", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openTheYard(page, '/');
  const badge = page.getByTestId('gate-badge');
  await expect(badge).toBeVisible();
  // The gate's word alone on a phone: the other two facts are in its name and the popover.
  expect(await badge.evaluate((element) => element.innerText.trim())).toMatch(
    /^Gate (green|pending)$/
  );
  const box = (await badge.boundingBox())!;
  const note = (await page.getByTestId('store-bar-note').boundingBox())!;
  expect(box.height).toBeGreaterThanOrEqual(44);
  expect(Math.abs(box.y + box.height / 2 - (note.y + note.height / 2))).toBeLessThan(box.height);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});

// #endregion gate-badge
