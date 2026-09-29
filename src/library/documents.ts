/**
 * Does:      Joins records.ts and pages.ts into DOCS, the one list every document is looked up in, and works out DocKey from them.
 * Does not:  Hold a document itself, or know where one sits in the sidebar.
 * Used by:   records.ts, pages.ts, sections.ts, addresses.ts, DocDialog.tsx, SideNav.tsx, Landing.tsx, SheetIcons.tsx, Shell.tsx, useAddressBar.ts, useNavigation.ts, useLiveBlocks.tsx.
 */
import { PAGES } from './pages';
import { RECORDS } from './records';

/** What a doc is. The phone drawer picks each row's icon from this (ADR-011 addendum). */
export type DocKind = 'overview' | 'adr' | 'infra' | 'changelog';

/** One document: the title its window shows, the words its sidebar row shows, where the API serves it. */
export type DocEntry = {
  title: string;
  menuLabel: string;
  url: string;
  kind: DocKind;
  number?: string;
};

// #region doc-key
/**
 * The name of a document in code, such as 'adrLockout' or 'readme'. It is
 * worked out from the two lists rather than typed out a second time, so a
 * document cannot be named without existing, and adding one to either list
 * is the whole of adding its name.
 */
export type DocKey = keyof typeof RECORDS | keyof typeof PAGES;

/** A name in both lists would be one document hiding the other, so this line stops compiling if there is one. */
export const NO_NAME_IN_BOTH: [Extract<keyof typeof RECORDS, keyof typeof PAGES>] extends [never]
  ? true
  : never = true;
// #endregion doc-key

// #region docs-record
// One record, one place. The URL's last segment is the slug the API looks up
// in DocsCatalog.cs, and a test fails if the two lists ever disagree, so a
// document can never appear in the menu without being servable, or the other
// way around (ADR-017). The documents themselves are in records.ts and
// pages.ts; this is the one list both are read through.
export const DOCS: Record<DocKey, DocEntry> = { ...RECORDS, ...PAGES };
// #endregion docs-record
