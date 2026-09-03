import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { signIn } from './signIn';

// The simulated room, end to end (ADR-027). The unit tests hold its rules; this
// holds the wiring, which is the part that spans a language boundary and so is
// the part a contract change breaks silently.

/** Open a live vehicle whose window ends hours out and bid the minimum. */
async function bidTheMinimum(page: import('@playwright/test').Page) {
  // Bidding belongs to an account now (ADR: Accounts and per-user bids), and a
  // fresh one per test is also what keeps these two from seeing each other's
  // bids. The room is still shared, which is the point of the second test.
  await signIn(page);
  // Most bids first: the default sort's top card can expire mid-test.
  await openTheYard(page, '/?status=live&sort=most-bids');
  await page.waitForSelector('article');
  await page.locator('article h3 button').first().click();
  await expect(page.getByText('Specifications')).toBeVisible();

  // Read the minimum, bid it, and if the server refuses, read it again.
  //
  // The room raises prices every eight seconds (ADR-027) and it is shared by
  // every spec in this file's process, so by the time this one runs it has been
  // bidding for a while. A round landing between reading the placeholder and
  // clicking the button makes the amount stale, and the server is right to
  // refuse it: a bid below the going rate is the defect that record's own
  // review found. What that leaves behind is a test whose bid silently did not
  // happen, failing later on a button that only exists once there is a bid.
  //
  // A real bidder reads the new number and bids again. So does this.
  //
  // What it waits for is the server's answer and not a number of seconds. The
  // first version of this waited six seconds for the accepted state and treated
  // anything else as a refusal, which is fine on a developer's machine and
  // wrong on a two-core runner where the same suite takes twice as long: a slow
  // accept was read as a refusal, and after three of those the helper threw.
  // Racing the two outcomes against each other returns as soon as either one
  // appears, so it is fast when the answer is fast and patient when the machine
  // is slow.
  const landed = page.getByText(/You're the high bidder at|You bought this vehicle/);
  const refused = page.getByRole('alert');
  for (let attempt = 1; attempt <= 3; attempt++) {
    const min = await page.locator('#bid-amount').getAttribute('placeholder');
    await page.locator('#bid-amount').fill(min!);
    await page.getByRole('button', { name: 'Place bid' }).click();

    const answer = await Promise.race([
      landed
        .first()
        .waitFor({ state: 'visible', timeout: 30_000 })
        .then(() => 'accepted')
        .catch(() => 'nothing'),
      refused
        .first()
        .waitFor({ state: 'visible', timeout: 30_000 })
        .then(() => 'refused')
        .catch(() => 'nothing'),
    ]);
    if (answer === 'accepted') {
      return;
    }
  }
  throw new Error(
    'three bids in a row were refused or unanswered: the room is raising faster than the page can answer'
  );
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
  await page
    .getByRole('button', { name: /Reset bids/ })
    .first()
    .click();

  // Neither side of the auction survives the reset: the buyer's bid is gone,
  // and so is the room's answer to it.
  await expect(page.getByRole('button', { name: /Reset bids/ })).toHaveCount(0);
  await expect(page.getByTestId('outbid-notice')).toHaveCount(0);
});
