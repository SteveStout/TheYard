import type { Page } from '@playwright/test';

/**
 * A browser with an account in it (ADR: Accounts and per-user bids).
 *
 * Registering through the API rather than through the form, because these are
 * tests about bidding and the room, and driving a sign-in form first would put
 * a second feature's failure mode in front of every one of them. The account
 * page's own spec drives the form. `page.request` shares the browsing
 * context's cookie jar, so the session lands where the page will send it, and
 * it goes through the Vite proxy so the cookie is on the page's own origin.
 */
export async function signIn(page: Page): Promise<string> {
  const email = `e2e-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.com`;
  const response = await page.request.post('/api/auth/register', {
    data: { email, password: 'correct horse' },
  });
  if (!response.ok()) {
    throw new Error(`could not register ${email}: ${response.status()} ${await response.text()}`);
  }
  return email;
}
