/**
 * Converts between the page address (the query string) and the State object the
 * page draws from, in both directions: parse() reads an address into a State and
 * serialize() writes a State back into an address. Nothing else in the app reads
 * or writes these query keys.
 *
 * Everything the page shows is decided by this State, and the State is kept only
 * in the address. That is why copying the address always gives a link that
 * reopens exactly the same view.
 *
 * The module uses no DOM and makes no network calls, so its compiled output can
 * be tested directly with `node --test`. (More in docs/ADR-005-state-lives-in-the-url.md.)
 */

export const SORTS = ['name', 'size', 'modified'] as const;
export const DIRS = ['asc', 'desc'] as const;
export const VIEWS = ['browse', 'docs'] as const;

export type Sort = (typeof SORTS)[number];
export type Dir = (typeof DIRS)[number];
export type View = (typeof VIEWS)[number];

export interface State {
  /** True when the address carries any known key, which means the dialog is open. */
  open: boolean;
  view: View;
  path: string;
  q: string;
  sort: Sort;
  dir: Dir;
  doc: string;
}

type Keys = Exclude<keyof State, 'open'>;

/**
 * The function a view calls to change the state. It passes only the keys that
 * change, and true as the second argument to overwrite the current history
 * entry instead of adding a new one.
 */
export type Navigate = (partial: Partial<State>, replaceEntry?: boolean) => void;

/**
 * The value of each key when the address does not set it: the file browser view,
 * the top folder, no search, no document, sorted by name in ascending order.
 * An address with none of these keys means the dialog is closed.
 */
export const DEFAULTS: Readonly<Omit<State, 'open'>> = Object.freeze({
  view: 'browse',
  path: '',
  q: '',
  sort: 'name',
  dir: 'asc',
  doc: '',
});
const KEYS = Object.keys(DEFAULTS) as Keys[];

function pick<T extends string>(value: string | null, allowed: readonly T[], fallback: T): T {
  return (allowed as readonly string[]).includes(value ?? '') ? (value as T) : fallback;
}

// #region parse
/**
 * Reads a query string into a State. Unknown keys are ignored. A value that is
 * not in its allowed list falls back to the default. A path is cleaned to forward
 * slashes with no leading or trailing slash, so two ways of writing the same
 * folder give the same State.
 * @param search the query string from the address, with or without the leading "?"
 */
export function parse(search: string): State {
  const params = new URLSearchParams(search.startsWith('?') ? search.slice(1) : search);
  return {
    // Any known key opens the dialog, so a link as short as "?path=docs" works. An open dialog
    // with every other value at its default is written as "?view=browse" by serialize().
    open: KEYS.some((key) => params.has(key)),
    view: pick(params.get('view'), VIEWS, DEFAULTS.view),
    path: cleanPath(params.get('path')),
    q: (params.get('q') ?? '').trim(),
    sort: pick(params.get('sort'), SORTS, DEFAULTS.sort),
    dir: pick(params.get('dir'), DIRS, DEFAULTS.dir),
    doc: (params.get('doc') ?? '').trim().toLowerCase(),
  };
}
// #endregion parse

/**
 * Writes a State as a query string. Values equal to their default are left out,
 * so links stay as short as possible. A closed State gives "" (no query at all).
 * An open State where every value is the default would also give nothing, so it
 * writes "?view=browse" to keep the dialog open when the address is read back.
 * @returns "" when closed, otherwise "?key=value&..."
 */
export function serialize(state: Partial<State>): string {
  const full: State = { ...DEFAULTS, open: true, ...state, path: cleanPath(state.path) };
  if (!full.open) {
    return '';
  }
  const params = new URLSearchParams();
  for (const key of KEYS) {
    if (full[key] !== DEFAULTS[key] && full[key] !== '') {
      params.set(key, full[key]);
    }
  }
  if (![...params.keys()].length) {
    params.set('view', full.view);
  }
  return `?${params.toString()}`;
}

/** True when the State says the dialog is open, meaning the address had at least one known key. */
export function isOpen(state: State): boolean {
  return state.open;
}

/**
 * Cleans a path into the form the API expects: backslashes turned into forward
 * slashes, empty segments removed, and no leading or trailing slash.
 * It leaves "." and ".." segments alone on purpose. The server checks every path
 * and refuses those, and the page then shows the server's error message.
 */
export function cleanPath(path: string | null | undefined): string {
  return (path ?? '')
    .replace(/\\/g, '/')
    .split('/')
    .filter((segment) => segment.length > 0)
    .join('/');
}

export interface Crumb {
  name: string;
  path: string;
}

/**
 * Lists every folder from the top down to the given path, for the breadcrumb
 * trail. The first entry is always "Home" with the empty path.
 */
export function crumbs(path: string): Crumb[] {
  const out: Crumb[] = [{ name: 'Home', path: '' }];
  let sofar = '';
  for (const segment of cleanPath(path).split('/').filter(Boolean)) {
    sofar = sofar ? `${sofar}/${segment}` : segment;
    out.push({ name: segment, path: sofar });
  }
  return out;
}

/**
 * Returns the parent folder of a path: "" when the path is directly under the
 * top folder, and null when the path is the top folder itself.
 */
export function parentOf(path: string): string | null {
  const clean = cleanPath(path);
  if (clean === '') {
    return null;
  }
  const slash = clean.lastIndexOf('/');
  return slash < 0 ? '' : clean.slice(0, slash);
}
