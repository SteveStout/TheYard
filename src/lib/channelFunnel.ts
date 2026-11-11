/**
 * Does:      Types the report GET /api/admin/channels answers from the card schema's Channels view (ADR: The Nightly
 *            Function on the Admin tab, the addendum on the funnel by channel): the newest weeks first, each with the
 *            six channels in one order, and gives the rows and the words the funnel by channel card prints.
 * Does not:  Read the view, fold it or attribute a reply to a channel (the server and the view do), or hold a company,
 *            a title or a person: counts only. No React in here.
 * Used by:   ChannelsCard.tsx.
 */

/** One channel's counts inside a week. */
export type ChannelCell = { channel: string; touches: number; replies: number; rooms: number };

/** One week, its Monday and the six channels in order. */
export type ChannelsWeek = { week: string; channels: ChannelCell[] };

/** The card's report, as GET /api/admin/channels answers it. */
export type ChannelsReport = {
  available: boolean;
  note: string | null;
  channels: string[];
  weeks: ChannelsWeek[];
  source: string;
};

/** One row of the card's table: a week and a channel, with the share of touches that drew a reply. */
export type ChannelRow = {
  key: string;
  week: string;
  channel: string;
  touches: number;
  replies: number;
  rooms: number;
  replyRate: string;
};

/** A reply rate as the card prints it: whole per cent of the touches, blank with no touch. */
export function replyRate(touches: number, replies: number): string {
  return touches > 0 ? `${Math.round((replies / touches) * 100)} %` : '';
}

/**
 * The table's rows: newest week first, its channels in the server's order, a channel with nothing in a week left
 * out so the table shows only what happened; a week with nothing at all keeps one row so the week is not lost.
 */
export function channelRows(report: Pick<ChannelsReport, 'weeks'>): ChannelRow[] {
  return report.weeks.flatMap((week) => {
    const moved = week.channels.filter((cell) => cell.touches + cell.replies + cell.rooms > 0);
    const cells = moved.length > 0 ? moved : week.channels.slice(0, 1);
    return cells.map((cell) => ({
      key: `${week.week} ${cell.channel}`,
      week: week.week,
      channel: cell.channel,
      touches: cell.touches,
      replies: cell.replies,
      rooms: cell.rooms,
      replyRate: replyRate(cell.touches, cell.replies),
    }));
  });
}

/** The line under the title: what the card counts. */
export const CHANNELS_MEASURE =
  'For each week, Monday first, the touches, replies from people and rooms booked of each channel: pounce, hotmgr, the email campaign, old contacts, applications and recruiters. A reply or a room counts for the channel that last touched the company.';
