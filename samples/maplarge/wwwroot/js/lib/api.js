// Every call to the server, in one place (ADR-004). A failure is an Error whose
// message is the problem document's `detail`, so the page shows the server's
// sentence and never a status code. Listings are cached by path for the life of
// the page and forgotten when anything under that path changes, which is what
// makes Back instant and a delete re-read once (ADR-006).

const listings = new Map();

/**
 * Reads a problem document into an Error, or a generic one when the body is
 * not a problem (a proxy's HTML page, a cut connection).
 * @param {Response} response
 */
export async function problemOf(response) {
  let detail = `${response.status} ${response.statusText}`.trim();
  try {
    const body = await response.json();
    detail = body.detail || body.title || detail;
  } catch {
    // Not JSON; the status line is the best sentence there is.
  }
  const error = new Error(detail);
  error.status = response.status;
  return error;
}

async function get(url) {
  const response = await fetch(url, { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw await problemOf(response);
  }
  return response.json();
}

async function send(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    throw await problemOf(response);
  }
  return response.status === 204 ? null : response.json();
}

// #region cache
/** One folder, from the cache when it is there. */
export async function browse(path) {
  if (!listings.has(path)) {
    listings.set(path, get(`/api/files?path=${encodeURIComponent(path)}`).catch((error) => {
      listings.delete(path);
      throw error;
    }));
  }
  return listings.get(path);
}

/** Forgets a folder and everything under it, after a write there. */
export function forget(path) {
  for (const key of [...listings.keys()]) {
    if (key === path || key.startsWith(`${path}/`) || path === '') {
      listings.delete(key);
    }
  }
}

/** Forgets the folder a path sits in and, for a folder, the folder itself. */
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

export async function move(from, to) {
  const result = await send('/api/files/move', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ from, to }) });
  forgetAround(from);
  forgetAround(to);
  return result;
}

export async function copy(from, to) {
  const result = await send('/api/files/copy', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ from, to }) });
  forgetAround(to);
  return result;
}

// #region upload
/**
 * Uploads one file with progress. XMLHttpRequest, because fetch cannot report
 * upload progress and a person watching a 50 MB file go up deserves a bar.
 * @param {string} path the receiving folder
 * @param {File} file
 * @param {boolean} overwrite
 * @param {(fraction: number) => void} onProgress 0 to 1
 * @returns {Promise<object>} the transfer result
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
      forget(path);
      if (request.status >= 200 && request.status < 300) {
        resolve(JSON.parse(request.responseText));
      } else {
        let detail = `${request.status} ${request.statusText}`;
        try {
          detail = JSON.parse(request.responseText).detail || detail;
        } catch {
          // A body that is not a problem document; keep the status line.
        }
        const error = new Error(detail);
        error.status = request.status;
        reject(error);
      }
    });
    request.addEventListener('error', () => reject(new Error('The upload did not reach the server.')));
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
