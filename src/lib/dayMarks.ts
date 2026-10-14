/**
 * The weekly rhythm on a daily chart: which slots fall on a weekend, so the
 * plot can shade them, and which carry a day-of-week label, so Monday and
 * Friday read at a glance. A month of dates labelled every sixth day says
 * nothing about which day was a Monday; a reviewer asking "was that quiet
 * stretch a weekend?" should not have to count. No React in here: a plain
 * function over the slot times, tested on its own.
 */

/** How a slot's day is told: the UTC calendar day of the instant, the same day the server keeps (ADR: Site activity). */
const UTC_DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'] as const;

/** The days that carry a label whatever the window: the ends of the working week. */
const LABELLED_DAYS = new Set<number>([1, 5]);

export type DayMark = {
  /** The slot's index along the axis. */
  index: number;
  /** The day of the week, Sun to Sat, in UTC. */
  day: (typeof UTC_DAY_NAMES)[number];
  /** Saturday or Sunday. */
  weekend: boolean;
  /** Monday or Friday: the label the axis draws. */
  labelled: boolean;
};

/** A slot's instant: a day as `2026-10-12` (read at noon UTC, so it is that day in every zone) or an ISO time. */
function instantOf(at: string): Date {
  return new Date(/^\d{4}-\d{2}-\d{2}$/.test(at) ? `${at}T12:00:00Z` : at);
}

// #region day-marks
/**
 * Every slot's day of the week, for a chart whose slots are days or parts of
 * days: the weekend ones to shade and the Mondays and Fridays to label. A
 * window shorter than a day gets no marks, because every slot is the same
 * day and shading it all would say nothing. A slot whose time cannot be read
 * is skipped rather than guessed.
 */
export function dayMarks(slots: { at: string }[], windowMinutes: number): DayMark[] {
  if (windowMinutes < 24 * 60 || slots.length === 0) return [];
  const marks: DayMark[] = [];
  let previousDay = -1;
  slots.forEach((slot, index) => {
    const when = instantOf(slot.at);
    if (Number.isNaN(when.getTime())) return;
    const day = when.getUTCDay();
    const weekend = day === 0 || day === 6;
    // A label sits on the first slot of its day, so a day of four-hour buckets is labelled once.
    const labelled = LABELLED_DAYS.has(day) && day !== previousDay;
    marks.push({ index, day: UTC_DAY_NAMES[day], weekend, labelled });
    previousDay = day;
  });
  return marks;
}
// #endregion day-marks

/**
 * The weekend as bands to shade: each run of weekend slots as one rectangle
 * from its first slot to its last, in slot indexes. The plot turns indexes
 * into x positions with its own geometry, so this knows nothing of pixels.
 */
export function weekendRuns(marks: DayMark[]): { from: number; to: number }[] {
  const runs: { from: number; to: number }[] = [];
  for (const mark of marks) {
    if (!mark.weekend) continue;
    const last = runs[runs.length - 1];
    if (last !== undefined && last.to === mark.index - 1) last.to = mark.index;
    else runs.push({ from: mark.index, to: mark.index });
  }
  return runs;
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/**
 * The labels along the bottom of a daily chart: one on every Monday and
 * Friday, the day's name and its date, with the month written once on the
 * first of them so the axis still says when the window is. "Mon 5 Oct, Fri 9,
 * Mon 12, Fri 16" reads the week off the axis; "5 Oct, 11 Oct, 17 Oct" did not.
 */
export function dayAxisLabels(
  marks: DayMark[],
  slots: { at: string }[]
): { index: number; text: string }[] {
  let first = true;
  return marks
    .filter((mark) => mark.labelled)
    .map((mark) => {
      const when = instantOf(slots[mark.index].at);
      const text = first
        ? `${mark.day} ${when.getUTCDate()} ${MONTHS[when.getUTCMonth()]}`
        : `${mark.day} ${when.getUTCDate()}`;
      first = false;
      return { index: mark.index, text };
    });
}
