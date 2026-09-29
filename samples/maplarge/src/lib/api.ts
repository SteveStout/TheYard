// Every call to the server, in one place (ADR-004). A failure is an ApiError
// whose message is the problem document's `detail`, so the page shows the
// server's sentence and never a status code. Listings are cached by path for
// the life of the page and forgotten when anything under that path changes,
// which is what makes Back instant and a delete re-read once (ADR-006).

import { ApiError, type DocEntry, type FileEntry, type FolderEntry, type Listing, type SearchResult, type TransferResult, type VersionInfo } from './types.js';

const listings = new Map<string, Promise<Listing>>();

/**
 * Reads a problem document into an error, or a generic one when the body is
 * not a problem (a proxy's HTML page, a cut connection).
 */
export async function problemOf(response: Response): Promise<ApiError> {
  let detail = `${response.status} ${response.statusText}`.trim();
  try {
    const body = (await response.json()) as { detail?: string; title?: string };
    detail = body.detail || body.title || detail;
  } catch {
    // Not JSON; the status line is the best sentence there is.
  }
  return new ApiError(detail, response.status);
}

async function get<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw await problemOf(response);
  }
  return (await response.json()) as T;
}

async function send<T>(url: string, options: RequestInit): Promise<T | null> {
  const response = await fetch(url, options);
  if (!response.ok) {
    throw await problemOf(response);
  }
  return response.status === 204 ? null : ((await response.json()) as T);
}

// #region cache
/** One folder, from the cache when it is there. */
export function browse(path: string): Promise<Listing> {
  let pending = listings.get(path);
  if (!pending) {
    pending = get<Listing>(`/api/files?path=${encodeURIComponent(path)}`).catch((error: unknown) => {
      listings.delete(path);
      throw error;
    });
    listings.set(path, pending);
  }
  return pending;
}

/** Forgets a folder and everything under it, after a write there. */
export function forget(path: string): void {
  for (const key of [...listings.keys()]) {
    if (key === path || key.startsWith(`${path}/`) || path === '') {
      listings.delete(key);
    }
  }
}

/** Forgets the folder a path sits in and, for a folder, the folder itself. */
export function forgetAround(path: string): void {
  const slash = path.lastIndexOf('/');
  forget(slash < 0 ? '' : path.slice(0, slash));
  forget(path);
}
// #endregion cache

export function search(path: string, q: string, limit?: number): Promise<SearchResult> {
  const params = new URLSearchParams({ path, q });
  if (limit) {
    params.set('limit', String(limit));
  }
  return get<SearchResult>(`/api/files/search?${params}`);
}

export function downloadUrl(path: string): string {
  return `/api/files/download?path=${encodeURIComponent(path)}`;
}

export async function createFolder(path: string, name: string): Promise<FolderEntry> {
  const result = await send<FolderEntry>(`/api/files/folder?path=${encodeURIComponent(path)}&name=${encodeURIComponent(name)}`, { method: 'POST' });
  forget(path);
  return result as FolderEntry;
}

export async function remove(path: string): Promise<void> {
  await send(`/api/files?path=${encodeURIComponent(path)}`, { method: 'DELETE' });
  forgetAround(path);
}

const json = { 'Content-Type': 'application/json' };

export async function move(from: string, to: string): Promise<FolderEntry | FileEntry> {
  const result = await send<FolderEntry | FileEntry>('/api/files/move', { method: 'POST', headers: json, body: JSON.stringify({ from, to }) });
  forgetAround(from);
  forgetAround(to);
  return result as FolderEntry | FileEntry;
}

export async function copy(from: string, to: string): Promise<FolderEntry | FileEntry> {
  const result = await send<FolderEntry | FileEntry>('/api/files/copy', { method: 'POST', headers: json, body: JSON.stringify({ from, to }) });
  forgetAround(to);
  return result as FolderEntry | FileEntry;
}

// #region upload
/**
 * Uploads one file with progress. XMLHttpRequest, because fetch cannot report
 * upload progress and a person watching a 50 MB file go up deserves a bar.
 * @param onProgress 0 to 1
 */
export function upload(path: string, file: File, overwrite: boolean, onProgress: (fraction: number) => void): Promise<TransferResult> {
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
        resolve(JSON.parse(request.responseText) as TransferResult);
      } else {
        let detail = `${request.status} ${request.statusText}`;
        try {
          detail = (JSON.parse(request.responseText) as { detail?: string }).detail || detail;
        } catch {
          // A body that is not a problem document; keep the status line.
        }
        reject(new ApiError(detail, request.status));
      }
    });
    request.addEventListener('error', () => reject(new ApiError('The upload did not reach the server.', 0)));
    request.send(form);
  });
}
// #endregion upload

export function docs(): Promise<DocEntry[]> {
  return get<DocEntry[]>('/api/docs');
}

export async function doc(slug: string): Promise<string> {
  const response = await fetch(`/api/docs/${encodeURIComponent(slug)}`);
  if (!response.ok) {
    throw await problemOf(response);
  }
  return response.text();
}

export function version(): Promise<VersionInfo> {
  return get<VersionInfo>('/api/version');
}
