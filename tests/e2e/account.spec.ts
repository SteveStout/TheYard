import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The account view end to end (ADR: Accounts and per-user bids): the form a
 * visitor actually uses, the bid that then belongs to them, and the sign-out
 * that takes the badges away. Every other spec registers through the API
 * because it is testing something else; this one does not, because the form is
 * the thing under test.
 */

/** A fresh address per run: the database outlives the test, so reuse collides. */
function anAddress(): string {
  return `form-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.com`;
}

/**
 * Click the button that registers, and wait for the request it makes rather
 * than for the heading that appears afterwards.
 *
 * The difference matters. Waiting on the heading gives Playwright's default
 * five seconds to cover a network round trip whose cost is dominated by a
 * password hash that is deliberately expensive: 120 ms on an idle machine,
 * measured, and longer when the whole browser suite is sharing the CPU with a
 * simulated room bidding over a hundred thousand vehicles. When it overran, the
 * failure said "heading not found", which is true and useless.
 *
 * Waiting on the response is not a longer timeout wearing a disguise. It waits
 * for the thing the test is actually blocked on, and it can say what the server
 * answered, so a rejected registration reads as a rejected registration instead
 * of as a missing heading.
 */
async function register(page: import('@playwright/test').Page, email: string) {
  const answered = page.waitForResponse(
    (response) =>
      response.url().includes('/api/auth/register') && response.request().method() === 'POST'
  );
  await page.getByRole('button', { name: 'Create an account' }).click();
  const response = await answered;
  expect(response.status(), await response.text()).toBe(200);
  await expect(page.getByRole('heading', { name: email })).toBeVisible();
}

test('the form creates an account, and the rail shows who is signed in', async ({ page }) => {
  const email = anAddress();
  await openTheYard(page, '/?view=account');

  await expect(page.getByRole('heading', { name: 'Sign in to bid' })).toBeVisible();
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('correct horse');
  await register(page, email);
  // The rail's account row is the address once there is one.
  await expect(page.getByRole('button', { name: email })).toBeVisible();

  // The session is a cookie, so a full reload finds the same person.
  await page.reload();
  await expect(page.getByRole('heading', { name: email })).toBeVisible();
});

test("a wrong password is refused in the server's own words", async ({ page }) => {
  const email = anAddress();
  await openTheYard(page, '/?view=account');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('correct horse');
  await register(page, email);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in to bid' })).toBeVisible();

  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('not the password');
  // Scoped to the form: the rail's account row also reads "Sign in" when
  // nobody is, which is right for a reader and ambiguous for a locator.
  // Wait on the login response for the same reason register() waits on its
  // own: the refusal costs a password hash, and under a loaded machine the
  // round trip has overrun the default five seconds (1.0.0.127, take two).
  const refused = page.waitForResponse(
    (response) =>
      response.url().includes('/api/auth/login') && response.request().method() === 'POST'
  );
  await page.locator('form').getByRole('button', { name: 'Sign in' }).click();
  const response = await refused;
  expect(response.status(), await response.text()).toBe(401);

  await expect(page.getByRole('alert')).toContainText('do not match an account');
});

test('a bid belongs to the account, and signing out takes it off the page', async ({ page }) => {
  test.setTimeout(60_000);
  const email = anAddress();

  await openTheYard(page, '/?view=account');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('correct horse');
  await register(page, email);
  await expect(page.getByText('Nothing yet')).toBeVisible();

  // Most bids first: the top card is live with a window ending hours out.
  await openTheYard(page, '/?status=live&sort=most-bids');
  await page.waitForSelector('article');
  await page.locator('article h3 button').first().click();
  await expect(page.getByText('Specifications')).toBeVisible();
  const min = await page.locator('#bid-amount').getAttribute('placeholder');
  await page.locator('#bid-amount').fill(min!);
  await page.getByRole('button', { name: 'Place bid' }).click();
  await expect(page.getByText(/You're the high bidder|You bought this vehicle/)).toBeVisible();

  // The account page lists it, named, and the row opens the vehicle again.
  // "(withdrawn)" is what the endpoint answers when it cannot find the vehicle
  // the bid is on, so its absence is the evidence that the join worked.
  await page.getByRole('button', { name: email }).click();
  const entry = page.getByTestId('history-entry');
  await expect(entry).toHaveCount(1);
  await expect(entry).not.toContainText('(withdrawn)');
  await entry.click();
  await expect(page.getByText('Specifications')).toBeVisible();

  // Signed out, the bids are somebody else's: the badges go, and the account
  // page offers the form again rather than an empty list.
  await page.getByRole('button', { name: email }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in to bid' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Reset bids/ })).toHaveCount(0);
});

// #region signed-out-bid
test('signed out, the vehicle page offers no bid form, and its one control opens the account view', async ({
  page,
}) => {
  // A bid belongs to an account and the server answers 401 to nobody's
  // (ADR: Accounts and per-user bids, addendum of 17 September). Until
  // 1.0.0.139 the page let a signed-out visitor fill the form and learn
  // that from the refusal. The rule the proof card follows applies here:
  // the control says what it needs and takes the visitor there.
  const listing = await page.request.get('/api/vehicles?status=live&sort=most-bids&limit=25');
  expect(listing.ok(), await listing.text()).toBe(true);
  const { vehicles } = (await listing.json()) as { vehicles: { id: string; sold: boolean }[] };
  const open = vehicles.find((vehicle) => !vehicle.sold);
  expect(open, 'none of the 25 most-bid live vehicles is unsold').toBeDefined();

  await openTheYard(page, `/?vehicle=${open!.id}`);
  await expect(page.getByText('Specifications')).toBeVisible();
  const panel = page.getByRole('region', { name: 'Auction' });
  await expect(panel.locator('#bid-amount')).toHaveCount(0);
  await expect(panel.getByRole('button', { name: 'Place bid' })).toHaveCount(0);
  await expect(panel.getByRole('button', { name: /Buy now/ })).toHaveCount(0);
  const signInToBid = panel.getByTestId('bid-sign-in');
  await expect(signInToBid).toBeEnabled();
  await expect(signInToBid).toHaveText('Sign in to bid');
  await signInToBid.click();
  await expect(page).toHaveURL(/view=account/);
  await expect(page.getByRole('heading', { name: 'Sign in to bid' })).toBeVisible();

  // Signed in, the same vehicle offers the form and the control is gone.
  const email = anAddress();
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('correct horse');
  await register(page, email);
  await openTheYard(page, `/?vehicle=${open!.id}`);
  await expect(panel.locator('#bid-amount')).toBeVisible();
  await expect(panel.getByRole('button', { name: 'Place bid' })).toBeVisible();
  await expect(panel.getByTestId('bid-sign-in')).toHaveCount(0);
});
// #endregion signed-out-bid

// #region forgot-password
test('forgot password answers one sentence, and says so when the site cannot send', async ({
  page,
}) => {
  // The browser suite's API has no sender configured, so the honest answer
  // here is the sentence about the operator; the emailed half is held by
  // the API tests with a recording sender (ADR: Accounts and per-user bids,
  // addendum).
  await openTheYard(page, '/?view=account');
  const forgot = page.getByTestId('forgot-password');
  await expect(forgot).toBeDisabled();
  await page.getByLabel('Email').fill(anAddress());
  await expect(forgot).toBeEnabled();
  await forgot.click();
  await expect(page.getByTestId('forgot-note')).toContainText('cannot send email');
});
// #endregion forgot-password
