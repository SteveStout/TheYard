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
