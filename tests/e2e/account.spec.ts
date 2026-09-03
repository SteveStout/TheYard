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
  await page.locator('form').getByRole('button', { name: 'Sign in' }).click();

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
