// The one parser and the one serializer for the state the URL carries (ADR-005).
// Pure: no DOM, no fetch, so node --test runs the compiled module as it is.
// Everything the page shows is a function of this object, and nothing the page
// knows is kept anywhere else, which is what makes a link to any view a link
// to that view.
export const SORTS = ['name', 'size', 'modified'];
export const DIRS = ['asc', 'desc'];
export const VIEWS = ['browse', 'docs'];
/** The state a bare address means: closed; and when open, home, no search, by name, ascending. */
export const DEFAULTS = Object.freeze({
    view: 'browse',
    path: '',
    q: '',
    sort: 'name',
    dir: 'asc',
    doc: '',
});
const KEYS = Object.keys(DEFAULTS);
function pick(value, allowed, fallback) {
    return allowed.includes(value ?? '') ? value : fallback;
}
// #region parse
/**
 * Reads a query string into a state. Unknown keys are dropped, a value outside
 * its list falls back to the default, and a path is normalised to forward
 * slashes with no leading or trailing one, so two spellings of one folder are
 * one state.
 * @param search the location's search, with or without the leading ?
 */
export function parse(search) {
    const params = new URLSearchParams(search.startsWith('?') ? search.slice(1) : search);
    return {
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
 * Writes a state as a query string, leaving out every value that is the
 * default, so a link is as short as it can be. A closed state is "" (the bare
 * address); an open state at home, where nothing else would be written, keeps
 * "?view=browse" so the address still says the browser is open.
 * @returns "" when closed, otherwise "?key=value&..."
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
/** True when the address asks for the browser to be open: any known key at all. */
export function isOpen(state) {
    return state.open;
}
/**
 * A path as the API wants it: forward slashes, no empty segments, no leading
 * or trailing slash. Never touches "." or ".."; the server refuses those and the
 * page shows its sentence.
 */
export function cleanPath(path) {
    return (path ?? '')
        .replace(/\\/g, '/')
        .split('/')
        .filter((segment) => segment.length > 0)
        .join('/');
}
/** The folders on the way to a path, for the breadcrumb, from home down to the path itself. */
export function crumbs(path) {
    const out = [{ name: 'Home', path: '' }];
    let sofar = '';
    for (const segment of cleanPath(path).split('/').filter(Boolean)) {
        sofar = sofar ? `${sofar}/${segment}` : segment;
        out.push({ name: segment, path: sofar });
    }
    return out;
}
/** The parent of a path, or "" at the top; null for home itself. */
export function parentOf(path) {
    const clean = cleanPath(path);
    if (clean === '') {
        return null;
    }
    const slash = clean.lastIndexOf('/');
    return slash < 0 ? '' : clean.slice(0, slash);
}
