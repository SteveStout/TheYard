/**
 * The log card's first line: how many lines of each level the window holds,
 * worst first, so an error is counted before the table is opened. No React in
 * here: one plain function, tested on its own.
 */

/** The levels the console logger writes, worst first; any other level follows them in the order it first appears. */
const ORDER = ['Critical', 'Error', 'Warning', 'Information', 'Debug', 'Trace'];

// #region level-line
/** "76 information lines", or "80 lines: 1 error, 3 warnings, 76 information": every level present, worst first, each with its count. */
export function levelLine(entries: { level: string }[]): string {
  if (entries.length === 0) return 'No lines in the window.';
  const counts = new Map<string, number>();
  for (const entry of entries) counts.set(entry.level, (counts.get(entry.level) ?? 0) + 1);
  const rank = (level: string) => {
    const at = ORDER.indexOf(level);
    return at === -1 ? ORDER.length : at;
  };
  const levels = [...counts.keys()].sort((a, b) => rank(a) - rank(b));
  const total = entries.length;
  const ending = total === 1 ? '' : 's';
  if (levels.length === 1) {
    return `${total.toLocaleString('en-US')} ${levels[0].toLowerCase()} line${ending}`;
  }
  const each = levels
    .map((level) => {
      const n = counts.get(level) ?? 0;
      const word = level.toLowerCase();
      const plural = n === 1 || word === 'information' || word === 'debug' || word === 'trace';
      return `${n.toLocaleString('en-US')} ${plural ? word : `${word}s`}`;
    })
    .join(', ');
  return `${total.toLocaleString('en-US')} line${ending}: ${each}`;
}
// #endregion level-line
