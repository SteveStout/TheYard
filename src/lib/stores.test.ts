import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchStores, note, otherSite, otherSiteAt, segments, type Stores } from './stores';

/**
 * The Store bar's seam (ADR: One container, both stores, and its addendum on
 * the toggle moving to the sites). What is worth holding here: the bar always
 * shows both families, the segment for the site the visitor is on is the one
 * marked current and is never a link, the other segment is a link to the
 * other site at the visitor's own path and query, and where the container
 * names no other site the other segment is drawn as not here rather than as a
 * dead control.
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
  other_site: 'https://theyard-cosmos.stevenstout.biz',
};

const home = { pathname: '/', search: '' };
const admin = { pathname: '/', search: '?view=admin' };

describe('segments', () => {
  it('marks the site the visitor is on and links the other segment to the other site at the same page', () => {
    const [sql, cosmos] = segments(both, admin);

    expect(sql).toMatchObject({ key: 'sql', current: true, href: null });
    expect(sql.title).toContain('This site');
    expect(sql.title).toContain('Azure SQL Database');
    expect(cosmos).toMatchObject({
      key: 'cosmos',
      current: false,
      href: 'https://theyard-cosmos.stevenstout.biz/?view=admin',
    });
    expect(cosmos.title).toContain('theyard-cosmos.stevenstout.biz');
  });

  it('follows the site, which is the default store, and not the store a header put this visit on', () => {
    // A measurement's header can put one request on the other store; the
    // site is still the container's default, and the bar says so.
    const [sql, cosmos] = segments({ ...both, current: 'cosmos' }, home);

    expect(sql.current).toBe(true);
    expect(cosmos).toMatchObject({
      current: false,
      href: 'https://theyard-cosmos.stevenstout.biz/',
    });
  });

  it('is the other way round on the other site', () => {
    const [sql, cosmos] = segments(
      {
        current: 'cosmos',
        stores: [
          { key: 'sql', name: 'Azure SQL Database', ready: true, default: false },
          { key: 'cosmos', name: 'Azure Cosmos DB', ready: true, default: true },
        ],
        other_site: 'https://theyard.stevenstout.biz/',
      },
      { pathname: '/', search: '?vehicle=12' }
    );

    expect(cosmos).toMatchObject({ current: true, href: null });
    expect(sql).toMatchObject({
      current: false,
      href: 'https://theyard.stevenstout.biz/?vehicle=12',
    });
  });

  it('draws the other segment as not here when the container names no other site, and never as a dead link', () => {
    const [sql, cosmos] = segments({ ...both, other_site: null }, home);

    expect(sql.current).toBe(true);
    expect(cosmos).toMatchObject({ current: false, href: null });
    expect(cosmos.title).toContain('no Cosmos DB site here');

    // The same on a container with one store and no setting at all, which is
    // a developer's machine.
    const [, alone] = segments({ current: 'sql', stores: [both.stores[0]] }, home);
    expect(alone).toMatchObject({ current: false, href: null });
  });
});

describe('note', () => {
  it('names the site, the store serving the page, and where accounts live', () => {
    expect(note(both)).toBe(
      'This is the SQL site, served from Azure SQL Database. Accounts and bids live in the store they were made in.'
    );
    expect(note({ ...both, current: 'cosmos' })).toBe(
      'This is the SQL site, served from Azure Cosmos DB. Accounts and bids live in the store they were made in.'
    );
  });

  it('tells the truth about a store that did not come up, and says nothing about a store it cannot find', () => {
    const down = { ...both.stores[1], ready: false };
    expect(note({ current: 'cosmos', stores: [both.stores[0], down] })).toContain(
      'Azure Cosmos DB did not come up'
    );
    expect(note({ current: 'nowhere', stores: both.stores })).toBe('');
  });
});

describe('otherSite', () => {
  it('turns the other site into an origin with its host, and nothing else into a link', () => {
    expect(otherSite({ ...both, other_site: 'https://theyard.stevenstout.biz/' })).toEqual({
      href: 'https://theyard.stevenstout.biz',
      host: 'theyard.stevenstout.biz',
    });
    expect(
      otherSite({ ...both, other_site: 'http://theyard-cosmos-ss.westus2.azurecontainer.io:8080' })
    ).toEqual({
      href: 'http://theyard-cosmos-ss.westus2.azurecontainer.io:8080',
      host: 'theyard-cosmos-ss.westus2.azurecontainer.io:8080',
    });
    expect(otherSite({ ...both, other_site: undefined })).toBeNull();
    expect(otherSite({ ...both, other_site: null })).toBeNull();
    expect(otherSite({ ...both, other_site: 'not an address' })).toBeNull();
    expect(otherSite({ ...both, other_site: 'javascript:alert(1)' })).toBeNull();
  });

  it('carries the path and the query to the other site, and only the origin from the setting', () => {
    expect(
      otherSiteAt({ ...both, other_site: 'https://theyard.stevenstout.biz/somewhere' }, admin)
    ).toBe('https://theyard.stevenstout.biz/?view=admin');
    expect(otherSiteAt({ ...both, other_site: null }, admin)).toBeNull();
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
