import { describe, expect, it } from 'vitest';
import {
  CARD_WINDOWS,
  cardUrl,
  cardWindowName,
  everyCardOn,
  keptLine,
  type KeptMeta,
  metaOf,
  stampFor,
} from './keptCards';

const meta = (over: Partial<KeptMeta>): KeptMeta => ({
  window: '7d',
  available: true,
  note: 'kept in Azure Cosmos DB for 35 days',
  total: 0,
  shown: 0,
  ...over,
});

describe('the cards over a window', () => {
  it('reads the ring for now and the kept endpoint for a window', () => {
    expect(cardUrl('sql', 'now', '/api/admin/sql')).toBe('/api/admin/sql');
    expect(cardUrl('sql', '30d', '/api/admin/sql')).toBe('/api/admin/kept?card=sql&window=30d');
    expect(CARD_WINDOWS.map(cardWindowName)).toEqual([
      'Now',
      'Last 24 hours',
      'Last 7 days',
      'Last 30 days',
    ]);
    expect(everyCardOn('now')).toEqual({ errors: 'now', logs: 'now', sql: 'now', store: 'now' });
  });

  it('says which of three things an empty table means', () => {
    expect(keptLine('now', null)).toContain('a roll empties');
    expect(keptLine('7d', null)).toBe('Reading the last 7 days…');
    // An answer for the window before this one is still not an answer for this one.
    expect(keptLine('30d', meta({ window: '7d', total: 9, shown: 9 }))).toBe(
      'Reading the last 30 days…'
    );
    expect(
      keptLine('7d', meta({ available: false, note: 'no document store is configured' }))
    ).toBe('Not kept here: no document store is configured.');
    expect(keptLine('7d', meta({}))).toBe(
      'Nothing in the last 7 days, kept in Azure Cosmos DB for 35 days.'
    );
  });

  it('says how many the window holds when the card shows fewer', () => {
    expect(keptLine('7d', meta({ total: 12, shown: 12 }))).toBe(
      'The last 7 days, kept in Azure Cosmos DB for 35 days: all 12.'
    );
    expect(keptLine('7d', meta({ total: 4321, shown: 200 }))).toBe(
      'The last 7 days, kept in Azure Cosmos DB for 35 days: the newest 200 of 4,321.'
    );
  });

  it('carries the answer into what a card says, and puts a date beside a time once there is one', () => {
    expect(
      metaOf(
        {
          card: 'sql',
          window: '24h',
          site: 'sql',
          store: 'Azure Cosmos DB',
          kept: { available: true, note: 'kept' },
          total: 3,
          shown: 3,
          entries: [],
        },
        '24h'
      )
    ).toEqual({ window: '24h', available: true, note: 'kept', total: 3, shown: 3 });
    expect(stampFor('7d', '2026-09-20T12:00:00Z')).toMatch(/^\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/);
    expect(stampFor('now', '2026-09-20T12:00:00Z')).not.toMatch(/^\d{2}-\d{2} /);
    expect(stampFor('7d', 'not a time')).toBe('');
  });
});
