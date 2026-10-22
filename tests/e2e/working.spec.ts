import { expect, test, type Page } from '@playwright/test';
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

  const lock = page.getByRole('dialog', { name: 'Working' });
  // Half a second after the request leaves, with room for a busy browser to draw it, and still
  // before the routed answer can arrive at 1,500 ms: seen here, it can only be the lock's wait.
  await expect(lock).toBeVisible({ timeout: 1_200 });
  await expect(lock).toContainText('Working');
  await expect(lock).toContainText('the yard is answering');
  // A screen reader hears it once, from the status line that is always in the page.
  await expect(page.getByTestId('working-said')).toHaveText('Working');

  // The page is inert under the lock: a click on a vehicle lands on the lock and opens nothing.
  await page.locator('article h3 button').first().click({ force: true });
  await expect(page).not.toHaveURL(/vehicle=/);

  await answered;
  await expect(lock).toBeHidden({ timeout: 2_000 });
  await expect(page.getByTestId('working-said')).toHaveText('');
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
/** Watches the page for the lock and says so through workingSeen; true once it has been seen. */
async function watchForTheLock(page: Page): Promise<() => boolean> {
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
  return () => seen;
}

const LIST = /\/api\/vehicles(\?|$)/;

test('a page nobody touches never locks when its listing refreshes itself', async ({ page }) => {
  // The page's own clock, so the listing's next refresh comes now and not in a minute.
  await page.clock.install();
  await openTheYard(page);
  await expect(page.locator('article').first()).toBeVisible();
  const seen = await watchForTheLock(page);
  await page.route(LIST, async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 1_500));
    await route.continue();
  });
  const asked = page.waitForRequest(LIST);
  const answered = page.waitForResponse(LIST, { timeout: 30_000 });
  await page.clock.fastForward('15:00');
  await asked;
  await answered;
  await page.waitForTimeout(800);
  expect(seen()).toBe(false);
});

test('a Retry click is a person asking, and it still locks while the answer is slow', async ({
  page,
}) => {
  await openTheYard(page);
  await expect(page.locator('article').first()).toBeVisible();
  await page.route(KIA, (route) =>
    route.fulfill({ status: 500, contentType: 'application/json', body: '{"detail":"down"}' })
  );
  await page.locator('select').nth(1).selectOption('Kia');
  const retry = page.getByRole('button', { name: 'Retry' });
  await expect(retry).toBeVisible();
  await page.unroute(KIA);
  await page.route(KIA, async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 1_500));
    await route.continue();
  });
  const answered = page.waitForResponse(KIA, { timeout: 30_000 });
  await retry.click();
  const lock = page.getByTestId('working-lock');
  await expect(lock).toHaveAttribute('data-locked', 'true', { timeout: 1_200 });
  await answered;
  await expect(lock).toHaveAttribute('data-locked', 'false', { timeout: 2_000 });
});
test('a request that hangs is given up at twenty seconds, the lock lets go and the error shows', async ({
  page,
}) => {
  test.setTimeout(60_000);
  await openTheYard(page, '/?view=account');
  await page.getByLabel('Email').fill('hang@example.com');
  await page.getByLabel('Password').fill('correct horse');
  await page.route('**/api/auth/login', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 25_000));
    // The page gave up long ago; the route has nobody left to answer.
    await route.abort().catch(() => {});
  });
  const lock = page.getByTestId('working-lock');
  const clicked = Date.now();
  await page.locator('form').getByRole('button', { name: 'Sign in' }).click();
  await expect(lock).toHaveAttribute('data-locked', 'true', { timeout: 1_200 });
  await expect(lock).toHaveAttribute('data-locked', 'false', { timeout: 23_000 });
  const heldFor = Date.now() - clicked;
  expect(heldFor).toBeGreaterThan(19_000);
  expect(heldFor).toBeLessThan(24_000);
  await expect(page.getByRole('alert')).toContainText('could not be reached');
});
// #endregion working-lock
