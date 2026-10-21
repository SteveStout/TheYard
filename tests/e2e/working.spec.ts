import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region working-lock
/**
 * The one working lock (ADR: One working lock for the whole site, after half a second): a
 * request a person started that runs past half a second covers the page with the lock, which takes
 * every click until the answer arrives and is gone after it; a fast answer never shows it.
 */
const KIA = /\/api\/vehicles\?.*make=Kia/;

test('a slow request a person started locks the page until it answers', async ({ page }) => {
  await openTheYard(page);
  await expect(page.locator('article').first()).toBeVisible();
  await page.route(KIA, async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 1_500));
    await route.continue();
  });
  const asked = page.waitForRequest(KIA);
  // The server's own time is added to the delay, and a busy run can make it long: the lock is held
  // to the answer, not to a clock.
  const answered = page.waitForResponse(KIA, { timeout: 30_000 });
  await page.locator('select').nth(1).selectOption('Kia');
  await asked;

  const lock = page.getByRole('status', { name: 'Working' });
  // Half a second after the request leaves, with room for a busy browser to draw it, and still
  // before the routed answer can arrive at 1,500 ms: seen here, it can only be the lock's wait.
  await expect(lock).toBeVisible({ timeout: 1_200 });
  await expect(lock).toContainText('Working');
  await expect(lock).toContainText('the yard is answering');

  // The page is inert under the lock: a click on a vehicle lands on the lock and opens nothing.
  await page.locator('article h3 button').first().click({ force: true });
  await expect(page).not.toHaveURL(/vehicle=/);

  await answered;
  await expect(lock).toBeHidden({ timeout: 2_000 });
  await expect(page).not.toHaveURL(/vehicle=/);
  await expect(page.getByTestId('result-count')).toHaveText(/of [\d,]+ vehicles/);
});

test('a fast answer never shows the lock', async ({ page }) => {
  await openTheYard(page);
  await expect(page.locator('article').first()).toBeVisible();
  let seen = false;
  await page.exposeFunction('workingSeen', () => {
    seen = true;
  });
  await page.evaluate(() => {
    new MutationObserver(() => {
      if (document.querySelector('[data-testid="working-lock"][data-locked="true"]')) {
        (window as unknown as { workingSeen: () => void }).workingSeen();
      }
    }).observe(document.body, { childList: true, subtree: true, attributes: true });
  });
  // Answered at once from here, so the test reads the half-second rule and not the server's speed.
  await page.route(KIA, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '{"total":0,"vehicles":[]}',
    })
  );
  const answered = page.waitForResponse(KIA);
  await page.locator('select').nth(1).selectOption('Kia');
  await answered;
  await page.waitForTimeout(800);
  expect(seen).toBe(false);
});
// #endregion working-lock
