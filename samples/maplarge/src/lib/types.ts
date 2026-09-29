/**
 * The wire, as TypeScript sees it: the same shapes Data/Entries.cs declares, in the same
 * snake_case, so a reader can hold the two files side by side (ADR-004). Nothing here is
 * computed; the server owns every derived fact.
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

/** A refusal from the server, carrying the problem document's status and its sentence. */
export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = 'ApiError';
  }
}
