// The operator's key, and where the browser keeps it (ADR: Site activity,
// and the line an address does not cross, fourth addendum). The key arrives
// once in the address bar, the app drops it from the address bar on the
// first render, and from then on this browser remembers it, so a plain
// /?view=admin on a phone that has opened the keyed URL once still shows the
// operator's cards and a bookmark saved after the page loaded cannot lose it.
// The key never leaves the browser except as the header the two keyed
// endpoints read.

export const ADMIN_KEY_STORAGE = 'theyard.admin-key';

/** The part of Storage this needs, so a test can hand in a map and a browser without storage can hand in null. */
export type KeyStorage = {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
};

/** The address bar wins and is remembered; otherwise what the browser remembered; otherwise nothing. A storage that throws counts as none. */
export function resolveAdminKey(search: string, storage: KeyStorage | null): string | null {
  const fromUrl = new URLSearchParams(search).get('key');
  if (fromUrl) {
    try {
      storage?.setItem(ADMIN_KEY_STORAGE, fromUrl);
    } catch {
      // Private mode, or storage disabled: the key still works for this page.
    }
    return fromUrl;
  }
  try {
    return storage?.getItem(ADMIN_KEY_STORAGE) ?? null;
  } catch {
    return null;
  }
}

// #region capture-at-startup
// The key is read before React draws anything (main.tsx), and not by the Admin
// tab itself. Since 1.0.3.0 the Admin tab arrives in a chunk of its own, which
// means it mounts a moment after the first render, and the first render is what
// takes `key` out of the address bar: a keyed link would have handed its key to
// a module that loaded after the key was gone.
let captured: string | null = null;

/** Reads the key out of the address bar and the browser's storage, once, at startup. */
export function captureAdminKey(
  storage: KeyStorage | null = browserStorage(),
  search: string = typeof window === 'undefined' ? '' : window.location.search
): string | null {
  captured = resolveAdminKey(search, storage);
  return captured;
}

/** The key this page has, as captured at startup; a later read of the address bar is the fallback. */
export function adminKey(): string | null {
  if (captured !== null) return captured;
  return typeof window === 'undefined'
    ? null
    : resolveAdminKey(window.location.search, browserStorage());
}

/** Test seam: forget what startup captured. */
export function clearCapturedAdminKey(): void {
  captured = null;
}
// #endregion capture-at-startup

/** Remember a key the operator typed into the page, trimmed; an empty entry remembers nothing. */
export function rememberAdminKey(entered: string, storage: KeyStorage | null): string | null {
  const key = entered.trim();
  if (!key) return null;
  try {
    storage?.setItem(ADMIN_KEY_STORAGE, key);
  } catch {
    // Private mode, or storage disabled: the key still works for this page.
  }
  return key;
}

/** Forget the key on this browser; the next visit needs the keyed URL again. */
export function forgetAdminKey(storage: KeyStorage | null): void {
  try {
    storage?.removeItem(ADMIN_KEY_STORAGE);
  } catch {
    // Nothing to forget where nothing could be kept.
  }
}

/** The browser's storage, or null where reading it throws. */
export function browserStorage(): KeyStorage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}
