import { expect, type Page } from '@playwright/test';

/**
 * Open a view of TheYard and wait until the application has actually loaded.
 *
 * Every spec here starts with a navigation and then asserts something on the
 * loaded app. Until the first inventory query answers, the view is the words
 * "Loading inventory", so each of those first assertions was also, quietly,
 * asserting that a cold start finishes inside Playwright's five-second default.
 * On a run where four workers arrive at the same server together it sometimes
 * does not, and the failure reads as whatever the test was named for: a keyboard
 * test failing before a key is pressed, a focus test failing because no vehicle
 * tile exists yet.
 *
 * The load gets its own budget here, once, where waiting is the actual subject.
 *
 * Two things about how it waits, both of which the first version got wrong.
 *
 * It waits for something to be **there**, not for something to be gone. The
 * first version waited for the text "Loading inventory" to reach a count of
 * zero, which is already true in the instant after `goto` resolves and before
 * React has mounted anything at all: `goto` returns on `load`, and the module
 * graph is fetched after that. So it passed immediately, having waited for
 * nothing, and handed the next assertion back its five seconds. It was also a
 * silent no-op on `?view=admin`, where that text never appears.
 *
 * And its budget is under the per-test timeout. The first version asked for
 * 45 seconds inside a 30-second test, so it could never spend what it claimed
 * to be giving; the test died first, with the generic message this helper exists
 * to replace.
 *
 * The announcement region is the signal because every view has one and it says
 * which view arrived, so this works for the inventory, the admin tab and the
 * account view alike (the staff review, 2026-09-03).
 */
export async function openTheYard(page: Page, path = '/'): Promise<void> {
  await page.goto(path);
  const announcement = page.getByTestId('view-announcement');
  // Present at all: React has mounted and rendered a view.
  await expect(announcement).toHaveCount(1, { timeout: 20_000 });
  // And settled: the announcement says "Loading inventory" only while the first
  // query is in flight, and names the view it arrived at once it is not.
  await expect(announcement).not.toHaveText('Loading inventory', { timeout: 20_000 });
}
