/**
 * Does:      Types the report GET /api/admin/pounce answers from the Function's PounceDay lines (ADR: The Nightly
 *            Function on the Admin tab, the addendum on time to first touch), seven Central days newest first, and
 *            gives the words the Time to first touch card prints, minutes as hours and days among them.
 * Does not:  Pick a day's line or judge it (the server does), or hold a company, a title or a person: counts only.
 *            No React in here.
 * Used by:   PounceCard.tsx.
 */

/** The server's judgement of a day. */
export type PounceJudgement = 'on_target' | 'late' | 'waiting' | 'quiet' | 'unread';

/** One Central day, as the route answers it. */
export type PounceDay = {
  day: string;
  read: boolean;
  caught: number;
  touched: number;
  median_min: number;
  worst_min: number;
  waiting: number;
  oldest_wait_min: number;
  judgement: PounceJudgement;
};

/** The card's report, as GET /api/admin/pounce answers it. */
export type FirstTouchReport = {
  available: boolean;
  note: string | null;
  days: PounceDay[];
  target_min: number;
  newest_ms: number | null;
  read_ms: number;
  source: string;
};

/** The words for each judgement. */
export const JUDGEMENT_WORDS: Record<PounceJudgement, string> = {
  on_target: 'on target',
  late: 'late',
  waiting: 'waiting',
  quiet: 'quiet',
  unread: 'no line',
};

const HOUR = 60;
const DAY = 24 * HOUR;

/**
 * Minutes as the card prints them: "45 min" up to 120, then hours and minutes ("3 h 5 min"), and over 48 hours days
 * and hours ("2 d 3 h").
 */
export function minutes(total: number): string {
  const whole = Math.max(0, Math.round(total));
  if (whole <= 2 * HOUR) {
    return `${whole} min`;
  }
  if (whole <= 2 * DAY) {
    const rest = whole % HOUR;
    return rest === 0 ? `${whole / HOUR} h` : `${Math.floor(whole / HOUR)} h ${rest} min`;
  }
  const hours = Math.floor((whole % DAY) / HOUR);
  return hours === 0 ? `${Math.floor(whole / DAY)} d` : `${Math.floor(whole / DAY)} d ${hours} h`;
}

/** A day's minute cell: blank when nothing it measures happened that day, so a zero is never read as an instant touch. */
export function minuteCell(count: number, value: number): string {
  return count > 0 ? minutes(value) : '';
}

/** The line under the title: what the card measures and the target. */
export function measureLine(targetMin: number): string {
  return `Minutes from a role going live to the first message reaching a hiring manager or VP, each Central day; the target is a median of ${minutes(targetMin)} or less, around the clock.`;
}

/** Whether any of the seven days has a line; none is the card's empty state. */
export function anyLine(report: Pick<FirstTouchReport, 'days'>): boolean {
  return report.days.some((day) => day.read);
}

/** The empty state's words. */
export const NO_LINE_YET = 'No role has been clocked yet.';
