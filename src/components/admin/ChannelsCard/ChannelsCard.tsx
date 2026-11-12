/**
 * Does:      Draws the funnel by channel (ADR: The Nightly Function on the Admin tab, the addendum on the funnel by
 *            channel): for each week, newest first, the touches, replies from people and rooms booked of pounce,
 *            hotmgr, the email campaign, old contacts, applications and recruiters, all read from GET
 *            /api/admin/channels. Read by the owner alone: no message ever links to it.
 * Does not:  Read the view, attribute a reply to a channel or add a number up (the server and the view do), or show a
 *            company, a title or a person: counts only.
 * Used by:   AdminBenchCard.tsx, through cardChunks.ts, on demand.
 */
import {
  CHANNELS_MEASURE,
  channelRows,
  type ChannelRow,
  type ChannelsReport,
} from '../../../lib/channelFunnel';
import styles from '../shared/card.module.css';
import { failed, useRead } from '../shared/common';
import { CardState } from '../shared/CardState';
import { type Column, DataTable } from '../DataTable';

/** A week and channel's row: the week, the channel, its three counts and its reply rate. */
const columns: Column<ChannelRow>[] = [
  { name: 'Week of', mono: true, cell: (row) => row.week },
  { name: 'Channel', rowHeader: true, cell: (row) => row.channel },
  { name: 'Touches', mono: true, short: true, cell: (row) => row.touches.toLocaleString('en-US') },
  { name: 'Replies', mono: true, short: true, cell: (row) => row.replies.toLocaleString('en-US') },
  { name: 'Reply rate', mono: true, short: true, cell: (row) => row.replyRate },
  { name: 'Rooms', mono: true, short: true, cell: (row) => row.rooms.toLocaleString('en-US') },
];

export default function ChannelsCard({ tick }: { tick: number }) {
  const report = useRead<ChannelsReport>('/api/admin/channels', tick);
  return (
    <article className={`${styles.wide} op-glass`} data-testid="channels-card">
      <h2 className={styles.cardTitle}>The funnel by channel</h2>
      <p className={styles.muted} data-testid="channels-measure">
        {CHANNELS_MEASURE}
      </p>
      {report === null ? (
        <CardState state="loading" />
      ) : report === 'failed' ? (
        failed('the funnel by channel')
      ) : !report.available ? (
        <p className={styles.muted} data-testid="channels-absent">
          {report.note ?? report.source}
        </p>
      ) : (
        <>
          <DataTable
            label="The funnel by channel, week by week"
            testId="channels-weeks"
            rows={channelRows(report)}
            rowKey={(row) => row.key}
            columns={columns}
            empty="no touch, reply or room in these weeks"
          />
          <p className={styles.muted} data-testid="channels-source">
            {report.note === null ? '' : `Not here yet: ${report.note}. `}Read from {report.source}.
          </p>
        </>
      )}
    </article>
  );
}
