import { expect, test } from '@playwright/test';
import { openTheYard } from './app';
import { signIn } from './signIn';

// The store toggle at the top of the page (ADR: One container, both stores).
// A local run on SQLite alone has one store and the toggle says so; the ship
// gate's run with the document store configured has two, and there the toggle
// is a switch: the page comes back on the other store, and an account made on
// one store reads as signed out on the other.

type Stores = { current: string; stores: { key: string; name: string; ready: boolean }[] };

async function stores(request: import('@playwright/test').APIRequestContext): Promise<Stores> {
  const response = await request.get('http://localhost:5210/api/stores');
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as Stores;
}

test('the toggle sits at the top of every view and names the store serving the visit', async ({
  page,
  request,
}) => {
  const answer = await stores(request);
  const current = answer.stores.find((store) => store.key === answer.current)!;

  await openTheYard(page, '/');
  const bar = page.getByTestId('store-bar');
  await expect(bar).toBeVisible();
  const group = bar.getByRole('radiogroup', { name: 'Store' });
  await expect(group.getByRole('radio', { checked: true })).toHaveAttribute(
    'data-store',
    answer.current
  );
  await expect(bar.getByTestId('store-bar-note')).toContainText(`served from ${current.name}`);
  // Both families are always drawn, so the choice reads as a choice.
  await expect(group.getByRole('radio')).toHaveCount(2);

  // The bar is above the view, so it is there on the Admin tab and the account view too.
  await openTheYard(page, '/?view=admin');
  await expect(page.getByTestId('store-bar')).toBeVisible();
  await openTheYard(page, '/?view=account');
  await expect(page.getByTestId('store-bar')).toBeVisible();
});

test('the toggle switches stores where there are two, and says the other is not here where there is one', async ({
  page,
  request,
}) => {
  const answer = await stores(request);
  const from = answer.current;
  const bar = page.getByTestId('store-bar');

  if (answer.stores.length < 2) {
    // One store: the other family is drawn as not here, and cannot be clicked.
    // Not a skip, because a toggle with one option is exactly the state this
    // run has to prove reads right (the ship gate runs the two-store shape).
    await openTheYard(page, '/');
    const other = bar.getByRole('radio', { checked: false });
    await expect(other).toBeDisabled();
    await expect(other).toHaveAttribute('title', /not on this container/);
    return;
  }

  const to = answer.stores.find((store) => store.key !== from)!;

  // An account on the store this visit starts on, made through the page's own
  // cookie jar so the account view shows it.
  const email = await signIn(page);
  await openTheYard(page, '/?view=account');
  await expect(page.getByRole('heading', { name: email })).toBeVisible();

  // Switch. The page reloads itself on the other store.
  await bar.getByRole('radio', { name: to.key === 'cosmos' ? 'Cosmos DB' : 'SQL' }).click();
  await expect(bar.getByRole('radio', { checked: true })).toHaveAttribute('data-store', to.key, {
    timeout: 20_000,
  });
  await expect(bar.getByTestId('store-bar-note')).toContainText(`served from ${to.name}`);
  // The account lives in the other store, so this store does not know it.
  await expect(page.getByRole('heading', { name: 'Sign in to bid' })).toBeVisible();

  // And back, where the session is still good.
  await bar.getByRole('radio', { name: from === 'cosmos' ? 'Cosmos DB' : 'SQL' }).click();
  await expect(bar.getByRole('radio', { checked: true })).toHaveAttribute('data-store', from, {
    timeout: 20_000,
  });
  await expect(page.getByRole('heading', { name: email })).toBeVisible();
});
