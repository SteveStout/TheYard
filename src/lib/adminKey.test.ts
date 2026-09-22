import { describe, expect, it } from 'vitest';
import {
  ADMIN_KEY_STORAGE,
  adminKey,
  captureAdminKey,
  clearCapturedAdminKey,
  forgetAdminKey,
  rememberAdminKey,
  resolveAdminKey,
  type KeyStorage,
} from './adminKey';

/**
 * Where the operator's key comes from (ADR: Site activity, and the line an
 * address does not cross, fourth addendum): the address bar first and it is
 * remembered, the browser's memory second, nothing third, and a storage that
 * throws is the same as no storage.
 */

function memory(initial: Record<string, string> = {}): KeyStorage & { map: Map<string, string> } {
  const map = new Map(Object.entries(initial));
  return {
    map,
    getItem: (k) => map.get(k) ?? null,
    setItem: (k, v) => void map.set(k, v),
    removeItem: (k) => void map.delete(k),
  };
}

describe('resolveAdminKey', () => {
  it('takes the key from the address bar and remembers it', () => {
    const storage = memory();
    expect(resolveAdminKey('?view=admin&key=abc', storage)).toBe('abc');
    expect(storage.map.get(ADMIN_KEY_STORAGE)).toBe('abc');
  });

  it('falls back to what the browser remembered, and to nothing', () => {
    expect(resolveAdminKey('?view=admin', memory({ [ADMIN_KEY_STORAGE]: 'kept' }))).toBe('kept');
    expect(resolveAdminKey('?view=admin', memory())).toBeNull();
    expect(resolveAdminKey('?view=admin', null)).toBeNull();
    expect(resolveAdminKey('?view=admin&key=', memory({ [ADMIN_KEY_STORAGE]: 'kept' }))).toBe(
      'kept'
    );
  });

  it('treats a storage that throws as no storage, in both directions', () => {
    const broken: KeyStorage = {
      getItem: () => {
        throw new Error('no');
      },
      setItem: () => {
        throw new Error('no');
      },
      removeItem: () => {
        throw new Error('no');
      },
    };
    expect(resolveAdminKey('?key=abc', broken)).toBe('abc');
    expect(resolveAdminKey('?view=admin', broken)).toBeNull();
    expect(() => forgetAdminKey(broken)).not.toThrow();
  });

  it('remembers a key typed into the page, trimmed, and nothing for an empty entry', () => {
    const storage = memory();
    expect(rememberAdminKey('  typed  ', storage)).toBe('typed');
    expect(resolveAdminKey('?view=admin', storage)).toBe('typed');
    expect(rememberAdminKey('   ', storage)).toBeNull();
    expect(resolveAdminKey('?view=admin', storage)).toBe('typed');
  });

  it('forgets on request', () => {
    const storage = memory({ [ADMIN_KEY_STORAGE]: 'kept' });
    forgetAdminKey(storage);
    expect(resolveAdminKey('?view=admin', storage)).toBeNull();
  });
});

describe('the key captured at startup', () => {
  it('survives the first render taking it out of the address bar', () => {
    // The Admin tab is a chunk of its own since 1.0.3.0 and mounts after that render.
    clearCapturedAdminKey();
    const storage = memory();
    // Startup reads the keyed address; the first render then drops the key from it.
    expect(captureAdminKey(storage, '?view=admin&key=from-the-link')).toBe('from-the-link');
    expect(adminKey()).toBe('from-the-link');
    expect(storage.map.get(ADMIN_KEY_STORAGE)).toBe('from-the-link');
    // Nothing in the address bar and nothing captured is nothing claimed.
    clearCapturedAdminKey();
    expect(captureAdminKey(memory(), '?view=admin')).toBeNull();
    clearCapturedAdminKey();
  });
});
