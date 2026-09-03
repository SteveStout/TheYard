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
 * This pays that cost once, in a place that is allowed to be slow, so the
 * five-second timeout in the specs measures what the specs are named for.
 * A server that is genuinely down still fails here, with a longer wait and a
 * clearer sentence than a heading that was never found.
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
