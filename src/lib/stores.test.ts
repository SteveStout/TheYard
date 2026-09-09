import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchStores, note, segments, selectStore, type Stores } from './stores';

/**
 * The store toggle's seam (ADR: One container, both stores). What is worth
 * holding here: the toggle always shows both families, a family this container
 * does not run is drawn as not here rather than left out, a store that did
 * not come up cannot be chosen, and the current store is never a button.
 */

function reply(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response;
}

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const both: Stores = {
  current: 'sql',
  stores: [
    { key: 'sql', name: 'Azure SQL Database', ready: true, default: true },
    { key: 'cosmos', name: 'Azure Cosmos DB', ready: true, default: false },
  ],
};

describe('segments', () => {
  it('marks the current store and offers the other one', () => {
    const [sql, cosmos] = segments(both);

    expect(sql).toMatchObject({
      key: 'sql',
      current: true,
      available: false,
      title: 'Azure SQL Database',
    });
    expect(cosmos).toMatchObject({
      key: 'cosmos',
      current: false,
      available: true,
      title: 'Azure Cosmos DB',
    });
  });

  it('draws a family this container does not run as not here', () => {
    const [, cosmos] = segments({ current: 'sql', stores: [both.stores[0]] });

    expect(cosmos).toMatchObject({ available: false, current: false });
    expect(cosmos.title).toContain('not on this container');
  });

  it('will not offer a store that did not come up, and says why', () => {
    const [, cosmos] = segments({
      current: 'sql',
      stores: [both.stores[0], { ...both.stores[1], ready: false }],
    });

    expect(cosmos.available).toBe(false);
    expect(cosmos.title).toContain('unavailable');
  });
});

describe('note', () => {
  it('names the store serving the page, and tells the truth about one that did not come up', () => {
    expect(note(both)).toBe(
      'This page is served from Azure SQL Database. Accounts and bids live in the store they were made in.'
    );

    const down = { ...both.stores[1], ready: false };
    expect(note({ current: 'cosmos', stores: [both.stores[0], down] })).toContain(
      'Azure Cosmos DB did not come up'
    );
    expect(note({ current: 'nowhere', stores: both.stores })).toBe('');
  });
});

describe('fetchStores', () => {
  it('answers the stores when the server does, and null when it cannot', async () => {
    fetchMock.mockResolvedValueOnce(reply(200, both));
    await expect(fetchStores()).resolves.toEqual(both);

    fetchMock.mockResolvedValueOnce(reply(503, {}));
    await expect(fetchStores()).resolves.toBeNull();

    fetchMock.mockRejectedValueOnce(new TypeError('offline'));
    await expect(fetchStores()).resolves.toBeNull();
  });
});

describe('selectStore', () => {
  it('posts the choice and hands back the stores as the server now sees them', async () => {
    fetchMock.mockResolvedValue(reply(200, { ...both, current: 'cosmos' }));

    const result = await selectStore('cosmos');

    expect(result).toEqual({ ok: true, stores: { ...both, current: 'cosmos' } });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/stores/select');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ store: 'cosmos' });
  });

  it("shows the server's own sentence when the store is not here", async () => {
    fetchMock.mockResolvedValue(
      reply(400, { detail: 'This container runs SQLite, and nothing called "cosmos".' })
    );

    await expect(selectStore('cosmos')).resolves.toEqual({
      ok: false,
      message: 'This container runs SQLite, and nothing called "cosmos".',
    });
  });

  it('says the server is unreachable rather than throwing at the bar', async () => {
    fetchMock.mockRejectedValue(new TypeError('offline'));

    const result = await selectStore('cosmos');

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.message).toContain('could not be reached');
  });
});
