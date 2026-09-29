// The one parser and the one serializer for the state the URL carries (ADR-005).
// Pure: no DOM, no fetch, so node --test runs it as it is. Everything the page
// shows is a function of this object, and nothing the page knows is kept
// anywhere else, which is what makes a link to any view a link to that view.

/** @typedef {{ open: boolean, view: 'browse'|'docs', path: string, q: string, sort: 'name'|'size'|'modified', dir: 'asc'|'desc', doc: string }} State */

export const SORTS = ['name', 'size', 'modified'];
export const DIRS = ['asc', 'desc'];
export const VIEWS = ['browse', 'docs'];

/** The state a bare address means: closed; and when open, home, no search, by name, ascending. */
export const DEFAULTS = Object.freeze({ view: 'browse', path: '', q: '', sort: 'name', dir: 'asc', doc: '' });
const KEYS = Object.keys(DEFAULTS);

// #region parse
/**
 * Reads a query string into a state. Unknown keys are dropped, a value outside
 * its list falls back to the default, and a path is normalised to forward
 * slashes with no leading or trailing one, so two spellings of one folder are
 * one state.
 * @param {string} search the location's search, with or without the leading ?
 * @returns {State}
 */
export function parse(search) {
  const params = new URLSearchParams(search.startsWith('?') ? search.slice(1) : search);
  const pick = (key, allowed) => {
    const value = params.get(key);
    return allowed.includes(value) ? value : DEFAULTS[key];
  };
  return {
    open: KEYS.some((key) => params.has(key)),
    view: pick('view', VIEWS),
    path: cleanPath(params.get('path')),
    q: (params.get('q') ?? '').trim(),
    sort: pick('sort', SORTS),
    dir: pick('dir', DIRS),
    doc: (params.get('doc') ?? '').trim().toLowerCase(),
  };
}
// #endregion parse

/**
 * Writes a state as a query string, leaving out every value that is the
 * default, so a link is as short as it can be. A closed state is "" (the bare
 * address); an open state at home, where nothing else would be written, keeps
 * "?view=browse" so the address still says the browser is open.
 * @param {Partial<State>} state
 * @returns {string} "" when closed, otherwise "?key=value&..."
 */
export function serialize(state) {
  const full = { ...DEFAULTS, open: true, ...state, path: cleanPath(state.path) };
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

/**
 * True when the address asks for the browser to be open: any known key at
 * all. A bare address shows the page with the dialog closed; a shared link
 * opens straight into the view it names.
 * @param {State} state
 */
export function isOpen(state) {
  return Boolean(state.open);
}

/**
 * A path as the API wants it: forward slashes, no empty segments, no leading
 * or trailing slash. Never touches "." or ".."; the server refuses those and the
 * page shows its sentence.
 * @param {string|null|undefined} path
 */
export function cleanPath(path) {
  return (path ?? '')
    .replace(/\\/g, '/')
    .split('/')
    .filter((segment) => segment.length > 0)
    .join('/');
}

/**
 * The folders on the way to a path, for the breadcrumb: [{name, path}] from
 * home down to the path itself.
 * @param {string} path
 */
export function crumbs(path) {
  const out = [{ name: 'Home', path: '' }];
  const segments = cleanPath(path).split('/').filter(Boolean);
  let sofar = '';
  for (const segment of segments) {
    sofar = sofar ? `${sofar}/${segment}` : segment;
    out.push({ name: segment, path: sofar });
  }
  return out;
}

/**
 * The parent of a path, or "" at the top; null for home itself.
 * @param {string} path
 */
export function parentOf(path) {
  const clean = cleanPath(path);
  if (clean === '') {
    return null;
  }
  const slash = clean.lastIndexOf('/');
  return slash < 0 ? '' : clean.slice(0, slash);
}
