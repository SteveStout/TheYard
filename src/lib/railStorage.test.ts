import { afterEach, describe, expect, it, vi } from 'vitest';
import { railStartsCollapsed, readRailStored, storeRailCollapsed } from './railStorage';

/** A window whose localStorage is a map, or one that throws, the way a private window can. */
function windowWith(seed: Record<string, string> | 'blocked') {
  const held = new Map(seed === 'blocked' ? [] : Object.entries(seed));
  const localStorage = {
    getItem: (key: string) => {
      if (seed === 'blocked') throw new Error('blocked');
      return held.get(key) ?? null;
    },
    setItem: (key: string, value: string) => {
      if (seed === 'blocked') throw new Error('blocked');
      held.set(key, value);
    },
  };
  vi.stubGlobal('window', { localStorage });
  return held;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the rail kept on this browser (ADR: The review route gives a reason before it asks)', () => {
  it('starts collapsed on a page that asks for it only when the browser keeps nothing, and never writes that default', () => {
    const held = windowWith({});
    expect(readRailStored()).toBeNull();
    expect(railStartsCollapsed(true)).toBe(true);
    expect(railStartsCollapsed(false)).toBe(false);
    expect(held.size).toBe(0);
  });

  it('honours a kept choice on every page', () => {
    windowWith({ 'theyard.rail': 'open' });
    expect(railStartsCollapsed(true)).toBe(false);
    windowWith({ 'theyard.rail': 'collapsed' });
    expect(railStartsCollapsed(false)).toBe(true);
    const held = windowWith({});
    storeRailCollapsed(false);
    expect(held.get('theyard.rail')).toBe('open');
  });

  it('reads a blocked store or a value it never wrote as nothing kept', () => {
    windowWith('blocked');
    expect(readRailStored()).toBeNull();
    expect(railStartsCollapsed(true)).toBe(true);
    windowWith({ 'theyard.rail': 'sideways' });
    expect(readRailStored()).toBeNull();
  });
});
