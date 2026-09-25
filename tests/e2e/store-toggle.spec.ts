import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// The Store bar at the top of the page (ADR: One container, both stores, and
// its addendum on the toggle moving to the sites). Each site is one store's
// site: the segment for the site the visitor is on is marked current and is
// not a control, and the other segment is a link to the other site at the same
// path and query. A container that names no other site, which is every local
// run and the ship gate, draws the other segment as not here; the deployed
// containers name each other, and the live check follows the link there.

type Stores = {
  current: string;
  stores: { key: string; name: string; ready: boolean; default: boolean }[];
  other_site: string | null;
};

const label = (key: string) => (key === 'cosmos' ? 'Cosmos DB' : 'SQL');

async function stores(request: import('@playwright/test').APIRequestContext): Promise<Stores> {
  const response = await request.get('http://localhost:5210/api/stores');
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as Stores;
}

test('the bar sits at the top of every view, marks this site and names the store serving it', async ({
  page,
  request,
}) => {
  const answer = await stores(request);
  const site = answer.stores.find((store) => store.default)!;
  const serving = answer.stores.find((store) => store.key === answer.current)!;

  await openTheYard(page, '/');
  const bar = page.getByTestId('store-bar');
  await expect(bar).toBeVisible();
  const nav = bar.getByRole('navigation', { name: 'Store' });
  // Both families are always drawn, so the choice reads as a choice, and the
  // site the visitor is on is the current one whatever store a header may
  // have put one request on.
  await expect(nav.locator('[data-store]')).toHaveCount(2);
  const current = nav.locator('[aria-current="page"]');
  await expect(current).toHaveAttribute('data-store', site.key);
  await expect(current).toHaveText(label(site.key));
  await expect(bar.getByTestId('store-bar-note')).toContainText(
    `This is the ${label(site.key)} site, served from ${serving.name}`
  );

  // The bar is above the view, so it is there on the Admin tab and the account view too.
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('store-bar')).toBeVisible();
  await openTheYard(page, '/?view=account');
  await expect(page.getByTestId('store-bar')).toBeVisible();
});

test('the other segment is a link to the other site at this same page, and never a switch in place', async ({
  page,
  request,
}) => {
  const answer = await stores(request);
  const site = answer.stores.find((store) => store.default)!;
  const otherKey = site.key === 'cosmos' ? 'sql' : 'cosmos';

  await openTheYard(page, '/?view=admin');
  const nav = page.getByTestId('store-bar').getByRole('navigation', { name: 'Store' });
  const other = nav.locator(`[data-store="${otherKey}"]`);
  await expect(other).toHaveText(label(otherKey));

  if (answer.other_site === null) {
    // No other site is named here, so the other segment is drawn as not here:
    // a disabled link with nowhere to go, and clicking it changes nothing.
    // Not a skip, because this is exactly the state every local run has to
    // prove reads right.
    await expect(other).toHaveAttribute('aria-disabled', 'true');
    await expect(other).not.toHaveAttribute('href', /.+/);
    await expect(other).toHaveAttribute('title', /no .* site here/);
    const before = page.url();
    await other.click({ force: true });
    await page.waitForTimeout(300);
    expect(page.url()).toBe(before);
    await expect(nav.locator('[aria-current="page"]')).toHaveAttribute('data-store', site.key);
  } else {
    // The deployed shape: the other segment carries the visitor's own path and
    // query to the other site, so two Admin tabs are one click apart.
    const origin = new URL(answer.other_site).origin;
    await expect(other).toHaveAttribute('href', `${origin}/?view=admin`);
    await expect(other).not.toHaveAttribute('aria-disabled', /.+/);
  }

  // The switch that set a cookie and reloaded the page in place is gone with
  // the cookie: a stale page that posts to it gets a refusal and no cookie.
  // 405 rather than 404, because the page's own fallback answers GET on every
  // path, so the path exists and the method does not.
  const gone = await request.post('http://localhost:5210/api/stores/select', {
    data: { store: otherKey },
  });
  expect(gone.status()).toBe(405);
  expect(gone.headers()['set-cookie']).toBeUndefined();
});

test('the bar is as tall before the stores answer as after, at a desk, whatever the note says', async ({
  page,
}) => {
  // The deployed sites answer /api/stores after the first paint and name Azure's stores, whose
  // note is longer than a local run's: 1.0.3.19 set it in spaced capitals and it wrapped to a
  // second line at 1280 when it arrived, moving every page 29 px (0.024 of shift). Here the
  // answer is held back and its store given a name longer than any real one.
  for (const width of [1024, 1280, 1440]) {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    await page.unrouteAll();
    await page.route('**/api/stores', async (route) => {
      const response = await route.fetch();
      const body = (await response.json()) as Stores;
      for (const store of body.stores) {
        store.name = `${store.name}, the store with the longest name this bar will ever be handed`;
      }
      await held;
      await route.fulfill({ response, json: body });
    });
    await page.setViewportSize({ width, height: 900 });
    await openTheYard(page, '/');
    const bar = page.getByTestId('store-bar');
    await expect(bar).toHaveAttribute('data-state', 'loading');
    const before = await bar.boundingBox();
    release();
    await expect(bar).toHaveAttribute('data-state', 'ready');
    const note = bar.getByTestId('store-bar-note');
    await expect(note).toHaveAttribute('title', /the longest name this bar will ever be handed/);
    const after = await bar.boundingBox();
    expect(after?.height, `the bar's height at ${width}`).toBe(before?.height);
  }
});

// #region no-ellipsis
// The tweaks pass (A3, A6): no ellipsis anywhere the gate reads. Under 1440 the note
// is the short form, the site and its store; from 1440, where it fits with the rail
// open and Azure's store names, the whole sentence on one line; at every width, the
// words the reader sees fit their box whole. The ready ring has
// its count in words beside it, and the whole count in its title.
test('the store bar ends no word in an ellipsis at 1024, 1280 or 1440, and says how many stores are ready', async ({
  page,
}) => {
  for (const width of [1024, 1280, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await openTheYard(page, '/');
    const bar = page.getByTestId('store-bar');
    await expect(bar).toHaveAttribute('data-state', 'ready');
    const read = await bar.getByTestId('store-bar-note').evaluate((note) => {
      const shown = Array.from(note.children).find(
        (child) => getComputedStyle(child).display !== 'none'
      );
      return {
        overflow: getComputedStyle(note).textOverflow,
        words: shown?.textContent ?? '',
        fits:
          shown === undefined ? false : shown.getBoundingClientRect().width <= note.clientWidth + 1,
      };
    });
    expect(read.overflow, `at ${width}`).not.toBe('ellipsis');
    expect(read.fits, `the note fits whole at ${width}: "${read.words}"`).toBe(true);
    if (width < 1440) expect(read.words).toMatch(/^(SQL|Cosmos DB) site · /);
    else expect(read.words).toMatch(/^This is the (SQL|Cosmos DB) site, served from /);
    await expect(bar.getByTestId('store-bar-ready-words')).toHaveText(/^\d\/\d ready$/i);
    await expect(bar.getByTestId('store-bar-ready-words').locator('..')).toHaveAttribute(
      'title',
      /^\d of \d stores ready$/
    );
  }
});
// #endregion no-ellipsis
