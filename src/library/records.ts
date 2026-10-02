/**
 * Does:      Lists every decision record the site serves, numbered, as data, joined from its parts in number order.
 * Does not:  Hold any other document, say which sidebar section shows a record, or draw anything.
 * Used by:   documents.ts, sections.test.ts, records001to030.ts, records031to060.ts, records061to087.ts.
 */
import type { DocEntry } from './documents';
import { RECORDS_001_TO_030 } from './decisionRecords/records001to030';
import { RECORDS_031_TO_060 } from './decisionRecords/records031to060';
import { RECORDS_061_TO_087 } from './decisionRecords/records061to087';

/**
 * A decision record is a document with a number. The type says so: a record
 * written without `kind: 'adr'` and a three-digit number does not compile, so
 * the sidebar can never show an unnumbered record. Each part in
 * src/library/decisionRecords holds its records to this type.
 */
export type RecordEntry = DocEntry & { kind: 'adr'; number: string };

// #region records
// Every decision record, in number order, joined from the runs in
// src/library/decisionRecords, one line per run. Each record's URL ends in the
// slug the API serves it under (api/TheYard.Api/DocumentationCatalog.cs), and
// DocumentationCatalogTests reads this file, every part and pages.ts to hold the
// lists to each other (ADR-017). The keys are the documents' names in code:
// DocKey is worked out from them in documents.ts, so adding a record to the last
// run is the whole of adding its name.
/** Every decision record the site serves, keyed by its name in code, in number order. */
export const RECORDS = {
  ...RECORDS_001_TO_030,
  ...RECORDS_031_TO_060,
  ...RECORDS_061_TO_087,
};
// #endregion records

/**
 * A name in two runs would be one record hiding the other once they are
 * joined, which one list could never do, so this line stops compiling if
 * there is one (the same guard documents.ts keeps over records and pages).
 */
export const NO_NAME_IN_TWO_RUNS: [
  | Extract<keyof typeof RECORDS_001_TO_030, keyof typeof RECORDS_031_TO_060>
  | Extract<keyof typeof RECORDS_001_TO_030, keyof typeof RECORDS_061_TO_087>
  | Extract<keyof typeof RECORDS_031_TO_060, keyof typeof RECORDS_061_TO_087>,
] extends [never]
  ? true
  : never = true;
