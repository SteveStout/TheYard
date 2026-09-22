import { expect, type Locator, type Page } from '@playwright/test';

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
// #region ribbons-off
/**
 * The ribbon ground (ADR: The glass look, the addendum on the ribbon ground) is
 * hidden on every page a spec opens, and glass.spec.ts's ribbon test asks for it
 * back with `window.__yardRibbons`. Measured 2026-09-21: with the ground on the
 * page, headless Chrome draws it and the glass over it in software, and the
 * browser pass alone (no .NET suite beside it) timed out twelve tests that took
 * seconds on 1.0.0.174, the landing page's first hundred among them.
 */
const groundHidden = new WeakSet<Page>();
async function hideTheGround(page: Page): Promise<void> {
  if (groundHidden.has(page)) return;
  groundHidden.add(page);
  await page.addInitScript(() => {
    addEventListener('DOMContentLoaded', () => {
      if ((window as unknown as { __yardRibbons?: boolean }).__yardRibbons) return;
      const style = document.createElement('style');
      style.textContent = '[data-testid^="ribbons"] { display: none !important; }';
      document.head.append(style);
    });
  });
}
// #endregion ribbons-off

// The inventory by default: since 1.0.1.0 a bare address opens the landing page,
// and nearly every spec here is about the inventory. landing.spec.ts opens '/'.
export async function openTheYard(page: Page, path = '/?view=inventory'): Promise<void> {
  await hideTheGround(page);
  await page.goto(path);
  const announcement = page.getByTestId('view-announcement');
  // Present at all: React has mounted and rendered a view. Thirty-five seconds, not
  // twenty: the gate for 1.0.0.173 had one test on each store still unmounted at twenty,
  // with the machine at 1.5 GB free and both sides of the gate running, and the page
  // mounted for every other test of the same run (ADR: The five-minute gate, the addendum
  // on every check running once).
  await expect(announcement).toHaveCount(1, { timeout: 35_000 });
  // And settled: the announcement says "Loading inventory" only while the first
  // query is in flight, and names the view it arrived at once it is not.
  await expect(announcement).not.toHaveText('Loading inventory', { timeout: 20_000 });
}

// #region sections
/**
 * Open the sidebar section a row lives in.
 *
 * Every section is a closed `details` since 1.0.0.135, so a spec that clicks a
 * document row has to say which section it is in first. Idempotent on purpose:
 * clicking a summary toggles, so a helper that always clicked would close the
 * section for the second row in the same test.
 */
export async function openSection(scope: Locator, label: string): Promise<void> {
  const summary = scope.locator('summary').filter({ hasText: label }).first();
  await expect(summary).toBeVisible();
  const open = await summary.evaluate((node) => (node.parentElement as HTMLDetailsElement).open);
  if (!open) {
    await summary.click();
  }
}

/** Open all of them, for the tests that count what the whole menu holds. */
export async function openAllSections(scope: Locator): Promise<void> {
  const summaries = scope.locator('summary');
  for (let index = 0; index < (await summaries.count()); index += 1) {
    const summary = summaries.nth(index);
    const open = await summary.evaluate((node) => (node.parentElement as HTMLDetailsElement).open);
    if (!open) {
      await summary.click();
    }
  }
}
// #endregion sections
