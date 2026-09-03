import { expect, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { signIn } from './signIn';

// Bidding, as every spec that bids has to do it.
//
// This lived inside market.spec until smoke.spec proved it was needed there
// too. smoke.spec had been reading the minimum off the placeholder and posting
// it once, with no retry, and passing: not because the race was not there, but
// because market.spec's reset used to clear the simulated room globally, which
// left smoke.spec a quiet field to bid into. Scoping that reset to its own user
// (ADR: Reset is one person's start-over) took the crutch away and smoke.spec
// failed twice in a row on the assertion after the bid.
//
// A test that passes because another test keeps clearing the world is not a
// passing test, and the two of them should not have had two answers to the same
// problem.

/** Open a live vehicle whose window ends hours out and bid the minimum. */
export async function bidTheMinimum(page: Page): Promise<void> {
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
