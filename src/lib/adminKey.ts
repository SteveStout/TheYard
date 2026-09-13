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
