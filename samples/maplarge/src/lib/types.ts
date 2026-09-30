/**
 * TypeScript types for the JSON the API sends. Each interface matches a C# record in
 * Data/Entries.cs field for field, using the same snake_case names the JSON uses, so the
 * two files can be compared side by side. This module only declares shapes and computes
 * nothing: every value, such as totals and timings, comes from the server.
 * (More in docs/ADR-004-the-wire.md.)
 */

export interface FolderEntry {
  name: string;
  path: string;
  modified_ms: number;
}

export interface FileEntry {
  name: string;
  path: string;
  size_bytes: number;
  modified_ms: number;
  extension: string;
}

export interface Totals {
  folder_count: number;
  file_count: number;
  total_bytes: number;
}

export interface Listing {
  path: string;
  parent: string | null;
  folders: FolderEntry[];
  files: FileEntry[];
  totals: Totals;
  took_ms: number;
}

export interface SearchResult {
  query: string;
  path: string;
  folders: FolderEntry[];
  files: FileEntry[];
  totals: Totals;
  truncated: boolean;
  took_ms: number;
}

export interface TransferResult {
  entries: FileEntry[];
  totals: Totals;
}

export interface DocEntry {
  slug: string;
  title: string;
  group: string;
}

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
