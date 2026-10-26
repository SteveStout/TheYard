/**
 * The nightly Function card's two added slots on the wire (ADR-102 and ADR-109, the addenda on the six
 * processes): the description, the process sections, a page of a run history, and the few plain functions the
 * page draws them with: the strip's thirty cells, the runs grouped by Central day, and the runs route's address.
 */

// #region cloud-processes
/** What the resource is, in one paragraph, and where it was read. */
export type CloudDescription = { text: string; source: string };

/** One switch as a chip: on is fine, shadow info, anything else off. */
export type CloudSwitch = { name: string; value: string; tone: 'fine' | 'info' | 'off' };

/** One labelled figure of a process section, its source under it. */
export type CloudFigure = { label: string; text: string; source: string };

/** One strip cell: a Central day's worst run, or skip where none ran. */
export type StripCell = 'ok' | 'late' | 'fail' | 'shadow' | 'skip';

/** One of the nightly Function's processes. */
export type CloudProcess = {
  key: string;
  name: string;
  tone: 'good' | 'info' | 'warning' | 'off';
  switches: CloudSwitch[];
  description: string;
  schedule: string;
  /** Last run, what it read and wrote, thirty days, next run. */
  figures: CloudFigure[];
  strip: StripCell[];
  strip_label: string;
  strip_total: string;
  all_href: string | null;
  history_count: string;
};

/** One page of a process's run history. */
export type ProcessRunsPage = {
  process: string;
  title: string;
  window: string;
  windows: string[];
  page: number;
  page_size: number;
  available: boolean;
  note: string | null;
  rows: ProcessRunRow[];
  more: boolean;
  count: string;
  totals: string;
  all_href: string | null;
};

/** One run as a row of the history. */
export type ProcessRunRow = {
  at_ms: number;
  day: string;
  when: string;
  took: string;
  outcome: string;
  came: string;
  tone: 'good' | 'info' | 'warning' | 'alarm' | 'quiet';
  sentence: string;
  trace_href: string | null;
};

/** How many cells the strip draws, one a Central day, today last; the server's ProcessSections.StripDays. */
export const PROCESS_STRIP_CELLS = 30;

/** The windows a history offers, with the words its pills print. */
export const PROCESS_WINDOWS: { key: string; label: string }[] = [
  { key: '24h', label: '24 h' },
  { key: '7d', label: '7 d' },
  { key: '30d', label: '30 d' },
];

/**
 * The strip as exactly PROCESS_STRIP_CELLS cells, oldest first: a short answer is padded with
 * hollow days on the left, a long one keeps its newest, and a word the page does not know is hollow.
 */
export function stripCells(strip: readonly string[]): StripCell[] {
  const known: StripCell[] = ['ok', 'late', 'fail', 'shadow', 'skip'];
  const cells = strip
    .slice(-PROCESS_STRIP_CELLS)
    .map((cell) => (known.includes(cell as StripCell) ? (cell as StripCell) : 'skip'));
  return [...Array<StripCell>(PROCESS_STRIP_CELLS - cells.length).fill('skip'), ...cells];
}

/** Each history row grouped under its Central day, in the order the page answered. */
export function runsByDay(
  rows: readonly ProcessRunRow[]
): { day: string; rows: ProcessRunRow[] }[] {
  const groups: { day: string; rows: ProcessRunRow[] }[] = [];
  for (const row of rows) {
    const last = groups[groups.length - 1];
    if (last !== undefined && last.day === row.day) last.rows.push(row);
    else groups.push({ day: row.day, rows: [row] });
  }
  return groups;
}

/** The rows with any run already listed left out, by its start and its trace, first seen kept. */
export function dedupe(rows: readonly ProcessRunRow[]): ProcessRunRow[] {
  const seen = new Set<string>();
  return rows.filter((row) => {
    const key = `${row.at_ms}|${row.trace_href ?? ''}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

/** The runs route for one process, window and page. */
export function processRunsUrl(process: string, window: string, page: number): string {
  return `/api/admin/cloud/nightly/runs/${encodeURIComponent(process)}?window=${window}&page=${page}`;
}
// #endregion cloud-processes
