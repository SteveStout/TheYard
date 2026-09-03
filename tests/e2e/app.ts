import { expect, type Page } from '@playwright/test';

/**
 * Open a view of TheYard and wait for the application to have finished loading.
 *
 * Every spec here starts with a navigation and then asserts something on the
 * loaded app. Until the first inventory query answers, the view is the words
 * "Loading inventory", so each of those first assertions was also, quietly,
 * asserting that a cold start finishes inside Playwright's five-second default.
 * It usually does. On a run where four workers arrive at the same server at the
 * same moment, it sometimes does not, and the failure reads as whatever the test
 * was named for: a keyboard test failing before a key is pressed, a focus test
 * failing because no vehicle tile exists yet.
 *
 * The load gets its own budget here, once, at the point where waiting is the
 * actual subject. Everything after it is measured against five seconds again,
 * which is the right budget for "did clicking this do the thing" and the wrong
 * one for "has the server finished starting".
 *
 * This is not a longer timeout in disguise. A server that is genuinely down
 * still fails, in this function, with a sentence that says the app never
 * finished loading rather than one about a missing button.
 */
export async function openTheYard(page: Page, path = '/'): Promise<void> {
  await page.goto(path);
  // Gone, not hidden: the notice is removed from the tree when the load ends.
  // A run that never shows it at all is already at zero and passes at once.
  await expect(page.getByText('Loading inventory')).toHaveCount(0, { timeout: 45_000 });
}
