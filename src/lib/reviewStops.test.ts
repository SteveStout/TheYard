import { describe, expect, it } from 'vitest';
import { docKeyForSlug } from '../library/addresses';
import { CARD_SLUGS } from './workbench';
import { minutesLeft, REVIEW_ADDRESS, REVIEW_REPORT_PATH, REVIEW_STOPS } from './reviewStops';

/** The views the app routes by ?view=, the ones a stop may name. */
const VIEWS = ['inventory', 'admin', 'account', 'blog', 'review'];

/** True when the site serves the address: the home page, a view, an Admin card, a document, or the resume's door. */
function served(address: string): boolean {
  if (address === '/' || address === '/resume') return true;
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
      'first-screen',
      'room',
      'spend',
      'decision',
      'gate',
      'stores',
      'resume',
    ]);
  });

  it('takes ten minutes, and counts down what is left from any stop', () => {
    expect(REVIEW_STOPS.reduce((sum, stop) => sum + stop.minutes, 0)).toBe(10);
    expect(minutesLeft(0)).toBe(10);
    expect(minutesLeft(2)).toBe(7);
    expect(minutesLeft(6)).toBe(1);
    expect(minutesLeft(7)).toBe(0);
    expect(minutesLeft(-3)).toBe(10);
  });

  it('opens only addresses this site serves, never a page elsewhere', () => {
    for (const stop of REVIEW_STOPS) {
      expect(stop.address.startsWith('/'), stop.address).toBe(true);
      expect(served(stop.address), stop.address).toBe(true);
    }
    expect(REVIEW_STOPS.map((stop) => stop.address)).toEqual([
      '/',
      '/?view=inventory',
      '/?view=admin&card=spend',
      '/?doc=adr-rules',
      '/?view=admin&card=tests',
      '/?doc=sql-vs-cosmos',
      '/resume',
    ]);
    // The check itself refuses what is not served.
    expect(served('/?view=admin&card=nope')).toBe(false);
    expect(served('/?doc=adr-nothing')).toBe(false);
    expect(served('https://github.com/SteveStout/TheYard/actions')).toBe(false);
  });

  it('writes every line as a sentence that ends in a full stop', () => {
    for (const stop of REVIEW_STOPS) {
      expect(stop.shows, stop.key).toMatch(/\.$/);
      expect(stop.doThis, stop.key).toMatch(/\.$/);
      expect(stop.title, stop.key).not.toMatch(/\.$/);
      expect(stop.minutes, stop.key).toBeGreaterThan(0);
    }
  });

  it('names its own address and the path a tagged open of it is kept under', () => {
    expect(REVIEW_ADDRESS).toBe('/?view=review');
    expect(REVIEW_REPORT_PATH).toBe('/review');
  });
});
