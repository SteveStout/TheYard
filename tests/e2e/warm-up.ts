import { chromium, type FullConfig } from '@playwright/test';

/**
 * Load the app once before the suite starts, and wait for real inventory.
 *
 * Every spec opens with `page.goto('/')` and then asserts something on the
 * inventory view. Until the first query answers, the view is the words
 * "Loading inventory", so those assertions were quietly also asserting that a
 * cold start finishes inside the five-second expect timeout. It usually does.
 * On the run where four workers hit an unwarmed Vite and an unwarmed API at the
 * same moment, it did not, and a11y.spec failed on line 9 with the keyboard
 * never pressed: a test named for the keyboard path reporting on load time.
 *
 * This was the first fix and it was too narrow: it only helps whichever test
 * goes first. The same failure came back two versions later in a different spec
 * partway through a run, and the general fix is `openTheYard` in ./app, which
 * every navigation goes through.
 *
 * This file stays because the two cover different costs. `openTheYard` waits for
 * the first inventory query to answer, which is an assertion timeout.
 * The navigation itself is what pays for Vite compiling the module graph, and
 * that is bounded by the navigation timeout instead, which no assertion budget
 * can widen. Paying it once here, with retries, keeps a cold runner from losing
 * its first `goto` to a compile.
 */
async function warmUp(config: FullConfig) {
  const baseURL = config.projects[0]?.use?.baseURL ?? 'http://localhost:5173';
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage();
  try {
    // webServer is up before this runs, but a retry costs nothing and removes
    // the ordering question entirely.
    for (let attempt = 1; attempt <= 5; attempt++) {
      try {
        await page.goto(baseURL, { timeout: 30_000 });
        break;
      } catch (error) {
        if (attempt === 5) throw error;
        await page.waitForTimeout(2_000);
      }
    }
    await page
      .getByRole('heading', { name: 'Inventory' })
      .waitFor({ state: 'visible', timeout: 120_000 });
  } finally {
    await page.close();
    await browser.close();
  }
}

export default warmUp;
