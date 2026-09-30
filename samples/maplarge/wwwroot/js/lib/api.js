/**
 * All HTTP calls from the page to the server live in this module, so the rest of
 * the code never builds a URL or reads a response by hand.
 *
 * When a call fails, it throws an ApiError whose message is the `detail` field of
 * the JSON error body the server sends. The page shows that sentence to the
 * person instead of a bare status code, because the server knows best what went
 * wrong.
 *
 * Folder listings are cached by path for as long as the page is open. Any write
 * (upload, delete, move, copy, new folder) removes the affected paths from the
 * cache. That makes the Back button instant while still re-fetching a folder
 * after it changes. (More in docs/ADR-004-the-wire.md.)
 */
import { ApiError } from './types.js';
const listings = new Map();
/**
 * Turns a failed response into an ApiError. It uses the `detail` (or `title`)
 * field of the server's JSON error body. When the body is not JSON, for example
 * an HTML error page from a proxy or a dropped connection, it falls back to the
 * HTTP status code and status text.
 */
export async function problemOf(response) {
    let detail = `${response.status} ${response.statusText}`.trim();
    try {
        const body = (await response.json());
        detail = body.detail || body.title || detail;
    }
    catch {
        // The body is not JSON, so keep the status code and text as the message.
    }
    return new ApiError(detail, response.status);
}
async function get(url) {
    const response = await fetch(url, { headers: { Accept: 'application/json' } });
    if (!response.ok) {
        throw await problemOf(response);
    }
    return (await response.json());
}
async function send(url, options) {
    const response = await fetch(url, options);
    if (!response.ok) {
        throw await problemOf(response);
    }
    return response.status === 204 ? null : (await response.json());
}
// #region cache
/**
 * Returns the listing of one folder, from the cache when it is there.
 * The cache stores the promise rather than the finished listing, so two callers
 * that ask for the same folder at the same moment share one request instead of
 * sending two. A request that fails removes itself from the cache, so the next
 * call tries again instead of getting the same failure back.
 */
export function browse(path) {
    let pending = listings.get(path);
    if (!pending) {
        pending = get(`/api/files?path=${encodeURIComponent(path)}`).catch((error) => {
            listings.delete(path);
            throw error;
        });
        listings.set(path, pending);
    }
    return pending;
}
/**
 * Removes a folder and every folder below it from the cache, after a write there.
 * A change can affect the listings of all folders underneath, and dropping them
 * is simpler and safer than trying to patch each cached listing. The empty path
 * "" is the top folder, so a write there clears the whole cache.
 */
export function forget(path) {
    for (const key of [...listings.keys()]) {
        if (key === path || key.startsWith(`${path}/`) || path === '') {
            listings.delete(key);
        }
    }
}
/**
 * Removes from the cache the parent folder of a path and the path itself (with
 * everything below it). These are the listings a move, copy or delete changes.
 */
export function forgetAround(path) {
    const slash = path.lastIndexOf('/');
    forget(slash < 0 ? '' : path.slice(0, slash));
    forget(path);
}
// #endregion cache
export function search(path, q, limit) {
    const params = new URLSearchParams({ path, q });
    if (limit) {
        params.set('limit', String(limit));
    }
    return get(`/api/files/search?${params}`);
}
export function downloadUrl(path) {
    return `/api/files/download?path=${encodeURIComponent(path)}`;
}
export async function createFolder(path, name) {
    const result = await send(`/api/files/folder?path=${encodeURIComponent(path)}&name=${encodeURIComponent(name)}`, { method: 'POST' });
    forget(path);
    return result;
}
export async function remove(path) {
    await send(`/api/files?path=${encodeURIComponent(path)}`, { method: 'DELETE' });
    forgetAround(path);
}
const json = { 'Content-Type': 'application/json' };
export async function move(from, to) {
    const result = await send('/api/files/move', { method: 'POST', headers: json, body: JSON.stringify({ from, to }) });
    forgetAround(from);
    forgetAround(to);
    return result;
}
export async function copy(from, to) {
    const result = await send('/api/files/copy', { method: 'POST', headers: json, body: JSON.stringify({ from, to }) });
    forgetAround(to);
    return result;
}
// #region upload
/**
 * Uploads one file and reports progress while it goes. It uses XMLHttpRequest
 * because fetch cannot report upload progress, and a large file needs a
 * progress bar so the person can see it is still moving.
 * @param path the folder to upload into
 * @param file the file to send
 * @param overwrite true to replace a file with the same name; with false the server answers 409
 * @param onProgress called with the fraction sent so far, from 0 to 1
 */
export function upload(path, file, overwrite, onProgress) {
    return new Promise((resolve, reject) => {
        const form = new FormData();
        form.append('files', file, file.name);
        const request = new XMLHttpRequest();
        request.open('POST', `/api/files/upload?path=${encodeURIComponent(path)}&overwrite=${overwrite ? 'true' : 'false'}`);
        request.upload.addEventListener('progress', (event) => {
            if (event.lengthComputable) {
                onProgress(event.loaded / event.total);
            }
        });
        request.addEventListener('load', () => {
            // Clear the cached folder whatever the outcome, not only on success. A 409 (name already
            // taken) means the cached listing is already out of date, because it did not show that name.
            forget(path);
            if (request.status >= 200 && request.status < 300) {
                resolve(JSON.parse(request.responseText));
            }
            else {
                let detail = `${request.status} ${request.statusText}`;
                try {
                    detail = JSON.parse(request.responseText).detail || detail;
                }
                catch {
                    // The body is not a JSON error, so keep the status code and text as the message.
                }
                reject(new ApiError(detail, request.status));
            }
        });
        request.addEventListener('error', () => reject(new ApiError('The upload did not reach the server.', 0)));
        request.send(form);
    });
}
// #endregion upload
export function docs() {
    return get('/api/docs');
}
export async function doc(slug) {
    const response = await fetch(`/api/docs/${encodeURIComponent(slug)}`);
    if (!response.ok) {
        throw await problemOf(response);
    }
    return response.text();
}
export function version() {
    return get('/api/version');
}
