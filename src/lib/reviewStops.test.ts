import { describe, expect, it } from 'vitest';
import { docKeyForSlug } from '../library/addresses';
import { CARD_SLUGS } from './workbench';
import { REVIEW_ADDRESS, REVIEW_REPORT_PATH, REVIEW_STOPS, tourLine } from './reviewStops';

/** The views the app routes by ?view=, the ones a stop may name. */
const VIEWS = ['inventory', 'admin', 'account', 'blog', 'review', 'recommendations'];

/** True when the site serves the address: the home page, a view, an Admin card, a document, or the resume itself. */
function served(address: string): boolean {
  if (address === '/' || address === '/resume' || address === '/resume?pdf=1') return true;
  if (!address.startsWith('/?')) return false;
  const params = new URLSearchParams(address.slice(2));
  const view = params.get('view');
  const doc = params.get('doc');
  if (doc !== null) return view === null && docKeyForSlug(doc) !== null;
  if (view === null || !VIEWS.includes(view)) return false;
  const card = params.get('card');
  return card === null || (view === 'admin' && (CARD_SLUGS as readonly string[]).includes(card));
}

describe('the review route (ADR: Ten minutes, seven stops)', () => {
  it('has seven stops, each with a key of its own, in the order a reviewer asks', () => {
    expect(REVIEW_STOPS).toHaveLength(7);
    expect(new Set(REVIEW_STOPS.map((stop) => stop.key)).size).toBe(7);
    expect(REVIEW_STOPS.map((stop) => stop.key)).toEqual([
      'quality',
      'first-screen',
      'room',
      'spend',
      'gate',
      'stores',
      'resume',
    ]);
  });

  it('takes ten minutes', () => {
    expect(REVIEW_STOPS.reduce((sum, stop) => sum + stop.minutes, 0)).toBe(10);
  });

  it('gives each tour cell its one line, word for word, with the live counts filled and none typed', () => {
    const counts = { vehicles: '100,000', recommendations: '13' };
    expect(REVIEW_STOPS.map((stop) => tourLine(stop, counts))).toEqual([
      'Built with AI, measured on every version.',
      'Live answer time, the Azure bill, time since the last roll, visitors.',
      '100,000 vehicles and a room that bids back.',
      'The bill by resource, shown in public.',
      'Every suite, both stores, CodeRabbit on the pull request.',
      'Azure SQL and Azure Cosmos DB, side by side.',
      'Two pages and 13 named recommendations.',
    ]);
    const before = { vehicles: '', recommendations: '' };
    expect(tourLine(REVIEW_STOPS[2], before)).toBe('Vehicles and a room that bids back.');
    expect(tourLine(REVIEW_STOPS[6], before)).toBe('Two pages and the named recommendations.');
    for (const stop of REVIEW_STOPS) expect(stop.line, stop.key).not.toMatch(/\d/);
  });

  it('opens only addresses this site serves, never a page elsewhere', () => {
    for (const stop of REVIEW_STOPS) {
      expect(stop.address.startsWith('/'), stop.address).toBe(true);
      expect(served(stop.address), stop.address).toBe(true);
    }
    expect(REVIEW_STOPS.map((stop) => stop.address)).toEqual([
      '/?doc=quality',
      '/',
      '/?view=inventory',
      '/?view=admin&card=spend',
      '/?view=admin&card=tests',
      '/?doc=sql-vs-cosmos',
      '/resume?pdf=1',
    ]);
    // The check itself refuses what is not served.
    expect(served('/?view=admin&card=nope')).toBe(false);
    expect(served('/?doc=adr-nothing')).toBe(false);
    expect(served('https://github.com/SteveStout/TheYard/actions')).toBe(false);
  });

  it('writes every line as a sentence that ends in a full stop', () => {
    for (const stop of REVIEW_STOPS) {
      expect(stop.line, stop.key).toMatch(/\.$/);
      expect(stop.title, stop.key).not.toMatch(/\.$/);
      expect(stop.minutes, stop.key).toBeGreaterThan(0);
    }
  });

  it('names its own address and the path a tagged open of it is kept under', () => {
    expect(REVIEW_ADDRESS).toBe('/?view=review');
    expect(REVIEW_REPORT_PATH).toBe('/review');
  });

  it('says only what its cards and a roll do: no blue-green, no link the card lacks (ADR: Blue-green on a $13 plan, measured and held)', () => {
    for (const each of REVIEW_STOPS) {
      expect(each.line, each.key).not.toMatch(
        /blue-green|no downtime|links to its place|on GitHub/
      );
    }
  });

  it('ends at the resume and who vouches for it', () => {
    expect(REVIEW_STOPS[REVIEW_STOPS.length - 1].title).toBe('The resume, and who vouches for it');
  });
});
