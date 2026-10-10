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

// #region room-to-answer
/** The fields of a listing row that decide whether the room can still answer. */
interface Candidate {
  id: string;
  starting_bid: number;
  current_bid: number | null;
  buy_now_price: number | null;
  auction_ends_at: number;
}

/** Two increments at the widest tier ($500 from $20,000 up): the bid, and the answer. */
const TWO_INCREMENTS = 1_000;

/**
 * Whether the room can still answer a minimum bid on this vehicle.
 *
 * The room stops at twice the opening ask and never crosses buy-now (ADR:
 * Competing bidders), so a vehicle standing at its ceiling takes a human's
 * bid and gets no answer, which is correct and is not what a test of the
 * answer wants to open. On a fresh store the first card always qualifies. On
 * the document store the test containers keep every run's bids for a day, so
 * the most-bid vehicle climbs a few increments per run until it stands exactly
 * there: measured at $78,000 against a $73,000 ceiling after a day of runs,
 * with the room silent and the test waiting forty-five seconds for it (ADR: A
 * second store on Cosmos DB). Five minutes on the clock is the same margin the
 * measurement script uses.
 */
export function roomCanAnswer(vehicle: Candidate, nowMs: number): boolean {
  const price = vehicle.current_bid ?? vehicle.starting_bid;
  const underCeiling = price + TWO_INCREMENTS < vehicle.starting_bid * 2;
  const underBuyNow =
    vehicle.buy_now_price === null || price + TWO_INCREMENTS < vehicle.buy_now_price;
  const timeLeft = vehicle.auction_ends_at - nowMs > 5 * 60_000;
  return underCeiling && underBuyNow && timeLeft;
}
// #endregion room-to-answer

// #region a-quiet-vehicle
/** The fields of a listing row that say whether anybody, the room included, has touched it. */
interface Quiet {
  id: string;
  current_bid: number | null;
  sold: boolean;
  auction_ends_at: number;
}

/**
 * A live vehicle nobody has bid on, with time left: the one to open when the
 * test is about the bid belonging to an account and not about the room.
 *
 * The room answers every bid within eight seconds (ADR: Competing bidders), and
 * it is busiest on the vehicle with the most bids, which is the card the account
 * spec used to open. Two specs in parallel workers then bid on the same vehicle:
 * market.spec through bidTheMinimum, which reads the minimum again when the
 * server refuses, and account.spec once, with no second read, so a round that
 * landed between its read and its click left it posting a stale minimum. The
 * page offered one figure and the server asked for the next, and the gate went
 * red twice on a test whose subject was never the room. A vehicle with no bid
 * has no room on it until this test bids, so the minimum it reads is the
 * minimum the server holds. Five minutes on the clock is the same margin
 * roomCanAnswer keeps.
 */
export async function aQuietVehicle(page: Page): Promise<string> {
  const listing = await page.request.get('/api/vehicles?status=live&limit=100');
  expect(listing.ok(), await listing.text()).toBe(true);
  const { vehicles } = (await listing.json()) as { vehicles: Quiet[] };
  const now = Date.now();
  const quiet = vehicles.find(
    (vehicle) =>
      vehicle.current_bid === null && !vehicle.sold && vehicle.auction_ends_at - now > 5 * 60_000
  );
  expect(
    quiet,
    'none of 100 live vehicles is unbid, unsold and five minutes from its end'
  ).toBeDefined();
  return quiet!.id;
}
// #endregion a-quiet-vehicle

/** Open a live vehicle the room can still answer on, and bid the minimum. */
export async function bidTheMinimum(page: Page): Promise<void> {
  // Bidding belongs to an account now (ADR: Accounts and per-user bids), and a
  // fresh one per test is also what keeps these two from seeing each other's
  // bids. The room is still shared, which is the point of the second test.
  await signIn(page);
  // Most bids first: the default sort's top card can expire mid-test. And not
  // the first card regardless: the first the room can still answer on.
  const listing = await page.request.get('/api/vehicles?status=live&sort=most-bids&limit=25');
  expect(listing.ok(), await listing.text()).toBe(true);
  const { vehicles } = (await listing.json()) as { vehicles: Candidate[] };
  const now = Date.now();
  const open = vehicles.find((vehicle) => roomCanAnswer(vehicle, now));
  expect(open, 'none of the 25 most-bid live vehicles has room under the ceiling').toBeDefined();
  await openTheYard(page, `/?vehicle=${open!.id}`);
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
