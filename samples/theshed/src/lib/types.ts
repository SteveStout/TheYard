/**
 * TypeScript types for the JSON the API sends. Each interface matches a C# record in
 * Data/ApiResponses.cs field for field, using the same snake_case names the JSON uses, so the
 * two files can be compared side by side. This module only declares shapes and computes
 * nothing: every value, such as totals and timings, comes from the server.
 * (More in docs/ADR-004-the-wire.md.)
 */

/** One folder in a listing or a search result; sent by GET /api/files and GET /api/files/search. */
export interface FolderEntry {
  name: string;
  path: string;
  modified_ms: number;
}

/** One file in a listing, a search result or an upload answer; sent by the same routes and by POST /api/files/upload. */
export interface FileEntry {
  name: string;
  path: string;
  size_bytes: number;
  modified_ms: number;
  extension: string;
}

/** The counts and the bytes under the view, computed by the server so the page never adds them up itself. */
export interface Totals {
  folder_count: number;
  file_count: number;
  total_bytes: number;
}

/** The answer to GET /api/files: one folder's contents, its parent, its totals and how long it took. */
export interface Listing {
  path: string;
  parent: string | null;
  folders: FolderEntry[];
  files: FileEntry[];
  totals: Totals;
  took_ms: number;
}

/** The answer to GET /api/files/search: every match under a folder, and whether the limit cut the list short. */
export interface SearchResult {
  query: string;
  path: string;
  folders: FolderEntry[];
  files: FileEntry[];
  totals: Totals;
  truncated: boolean;
  took_ms: number;
}

/** The answer to POST /api/files/upload: the files written and the folder's new totals. */
export interface TransferResult {
  entries: FileEntry[];
  totals: Totals;
}

/** One document in GET /api/docs, the list the Docs tab shows. */
export interface DocumentEntry {
  slug: string;
  title: string;
  group: string;
}

/** The answer to GET /api/version: the version from the changelog and the commit it was built from. */
export interface VersionInfo {
  version: string;
  commit: string;
}

/**
 * An error returned by the server. The message is the human-readable sentence from the
 * server's JSON error body, and status is the HTTP status code (0 when the request never
 * reached the server). Code can check status, for example 409 for a name already taken.
 */
export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = 'ApiError';
  }
}
