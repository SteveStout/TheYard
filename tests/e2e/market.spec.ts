import { expect, test } from '@playwright/test';

// The simulated room, end to end (ADR-027). The unit tests hold its rules; this
// holds the wiring, which is the part that spans a language boundary and so is
// the part a contract change breaks silently.

/** Open a live vehicle whose window ends hours out and bid the minimum. */
async function bidTheMinimum(page: import('@playwright/test').Page) {
  // Most bids first: the default sort's top card can expire mid-test.
  await page.goto('/?status=live&sort=most-bids');
  await page.waitForSelector('article');
  await page.locator('article h3 button').first().click();
  await expect(page.getByText('Specifications')).toBeVisible();
  const min = await page.locator('#bid-amount').getAttribute('placeholder');
  await page.locator('#bid-amount').fill(min!);
  await page.getByRole('button', { name: 'Place bid' }).click();
}

test('a bid is answered by the room, and the lead changes hands', async ({ page }) => {
  // The page asks for a round every eight seconds and the API's grace period is
  // zero under the test servers, so one round is enough.
  test.setTimeout(90_000);

  await bidTheMinimum(page);

  // A minimum bid that crosses buy-now wins outright under BidRules, which ends
  // the auction. The room must leave that vehicle alone, and that is worth
  // asserting rather than skipping past.
  if (await page.getByText(/You bought this vehicle/).isVisible()) {
    await page.waitForTimeout(12_000);
    await expect(page.getByTestId('outbid-notice')).toHaveCount(0);
    return;
  }

  await expect(page.getByText(/You're the high bidder at/)).toBeVisible();

  // This is the assertion the whole feature exists for: before it, the chip
  // could never come off.
  await expect(page.getByTestId('outbid-notice')).toBeVisible({ timeout: 45_000 });
  await expect(page.getByTestId('outbid-notice')).toContainText('Someone outbid you');
  await expect(page.getByText(/You're the high bidder at/)).toHaveCount(0);
});

test('resetting clears the room along with the buyer', async ({ page }) => {
  test.setTimeout(90_000);

  await bidTheMinimum(page);
  await expect(page.getByRole('button', { name: /Reset bids/ }).first()).toBeVisible();

  await page.getByRole('button', { name: 'Back to inventory' }).click();
  // Reset asks for confirmation, and Playwright dismisses dialogs unless told
  // otherwise, so without this the click does nothing at all.
  page.on('dialog', (dialog) => void dialog.accept());
  await page.getByRole('button', { name: /Reset bids/ }).first().click();

  // Neither side of the auction survives the reset: the buyer's bid is gone,
  // and so is the room's answer to it.
  await expect(page.getByRole('button', { name: /Reset bids/ })).toHaveCount(0);
  await expect(page.getByTestId('outbid-notice')).toHaveCount(0);
});
